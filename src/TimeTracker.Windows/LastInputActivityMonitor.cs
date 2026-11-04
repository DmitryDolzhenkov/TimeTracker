using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeTracker.Core;
using TimeTracker.Core.Abstractions;

namespace TimeTracker.Windows;

public sealed class LastInputActivityMonitor : BackgroundService, IActivityMonitor
{
    private readonly TimeProvider _time;
    private readonly TrackerOptions _options;
    private readonly ILogger<LastInputActivityMonitor> _logger;
    private readonly IdleDetector _detector;

    public LastInputActivityMonitor(TimeProvider time, IOptions<TrackerOptions> options, ILogger<LastInputActivityMonitor> logger)
    {
        _time = time;
        _options = options.Value;
        _logger = logger;
        _detector = new IdleDetector(_options.IdleThreshold);
    }

    public event Action<DateTimeOffset>? IdleDetected;
    public event Action? InputResumed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                Poll();
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка.
        }
    }

    private void Poll()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            _logger.LogWarning("GetLastInputInfo вернул ошибку {Error}", Marshal.GetLastWin32Error());
            return;
        }

        // Время "сейчас" читается после времени ввода, чтобы разность не ушла в минус.
        var nowTick = unchecked((uint)Environment.TickCount64);

        switch (_detector.Evaluate(info.Time, nowTick, out var idle))
        {
            case IdleSignal.IdleDetected:
                IdleDetected?.Invoke(_time.GetUtcNow() - idle);
                break;
            case IdleSignal.InputResumed:
                InputResumed?.Invoke();
                break;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}
