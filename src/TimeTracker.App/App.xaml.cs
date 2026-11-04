using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TimeTracker.Core;
using TimeTracker.Core.Abstractions;
using TimeTracker.Infrastructure;
using TimeTracker.Windows;

namespace TimeTracker.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private IHost? _host;
    private TrackerService? _tracker;
    private MainWindow? _mainWindow;
    private LogWindow? _logWindow;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Второй экземпляр писал бы в тот же журнал и принял бы работу первого за сбой.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\TimeTracker.App", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Time Tracker уже запущен. Значок находится в области уведомлений.", "Time Tracker");
            Shutdown();
            return;
        }

        try
        {
            _host = BuildHost(e.Args);

            // Вне потока UI: SystemEvents тогда заводит собственный поток с циклом сообщений.
            Task.Run(() => _host.StartAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось запустить приложение:\n{ex.Message}", "Time Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _tracker = _host.Services.GetRequiredService<TrackerService>();
        _tracker.Changed += OnTrackerChanged;

        _mainWindow = new MainWindow(_tracker);
        _tray = new TrayIcon(_tracker, ShowMain);
        _mainWindow.Show();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);

        // WPF может завершить приложение раньше, чем сработает обработчик SystemEvents. Повторный вызов игнорируется.
        _tracker?.NotifySessionEnding();
        AllowWindowsToClose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();

        if (_host is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                Task.Run(() => _host.StopAsync(timeout.Token)).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Журнал уже сброшен на диск, дольше ждать незачем.
            }
            _host.Dispose();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    public void ShowMain()
    {
        if (_mainWindow is null)
            return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ShowLog()
    {
        if (_tracker is null)
            return;

        if (_logWindow is null)
        {
            _logWindow = new LogWindow(_tracker);
            _logWindow.Closed += (_, _) => _logWindow = null;
            _logWindow.Show();
        }
        _logWindow.Activate();
    }

    public void ShutdownApplication()
    {
        AllowWindowsToClose();
        Shutdown();
    }

    private void AllowWindowsToClose()
    {
        if (_mainWindow is not null)
            _mainWindow.AllowClose = true;
    }

    private void OnTrackerChanged()
    {
        // Shutdown терминален для процесса, какой бы причиной он ни был вызван.
        if (_tracker?.State == TimerState.Shutdown)
            Dispatcher.InvokeAsync(ShutdownApplication);
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();

        var section = builder.Configuration.GetSection("Tracker");
        builder.Services.Configure<TrackerOptions>(section);

        var dataDirectory = section[nameof(TrackerOptions.DataDirectory)]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTrackerSimple");

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IEventStore>(services => new JsonLinesEventStore(
            Path.Combine(dataDirectory, "events.jsonl"),
            services.GetRequiredService<ILogger<JsonLinesEventStore>>()));
        builder.Services.AddSingleton<IHeartbeatStore>(_ => new FileHeartbeatStore(Path.Combine(dataDirectory, "heartbeat.txt")));

        builder.Services.AddSingleton<LastInputActivityMonitor>();
        builder.Services.AddSingleton<IActivityMonitor>(services => services.GetRequiredService<LastInputActivityMonitor>());
        builder.Services.AddSingleton<SystemEventsSessionSource>();
        builder.Services.AddSingleton<ISessionEventsSource>(services => services.GetRequiredService<SystemEventsSessionSource>());
        builder.Services.AddSingleton<TrackerService>();

        // Порядок важен: TrackerService должен подписаться на сигналы раньше, чем они начнут поступать.
        builder.Services.AddHostedService(services => services.GetRequiredService<TrackerService>());
        builder.Services.AddHostedService(services => services.GetRequiredService<LastInputActivityMonitor>());
        builder.Services.AddHostedService(services => services.GetRequiredService<SystemEventsSessionSource>());

        return builder.Build();
    }
}
