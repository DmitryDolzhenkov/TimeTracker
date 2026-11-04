using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using TimeTracker.Core.Abstractions;

namespace TimeTracker.Windows;

public sealed class SystemEventsSessionSource : IHostedService, ISessionEventsSource
{
    public event Action? Locked;
    public event Action? Unlocked;
    public event Action? Ending;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.SessionEnding += OnSessionEnding;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.SessionEnding -= OnSessionEnding;
        return Task.CompletedTask;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.RemoteDisconnect:
            case SessionSwitchReason.ConsoleDisconnect:
                Locked?.Invoke();
                break;

            // Отключение считаем блокировкой, поэтому подключение должно её снимать:
            // после него SessionUnlock приходит не всегда, и таймер нельзя было бы запустить.
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.RemoteConnect:
            case SessionSwitchReason.ConsoleConnect:
                Unlocked?.Invoke();
                break;
        }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => Ending?.Invoke();
}
