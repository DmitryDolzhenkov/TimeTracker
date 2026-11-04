using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeTracker.Core.Abstractions;
using TimeTracker.Core.Events;

namespace TimeTracker.Core;

public enum AdjustResult { Applied, InvalidMinutes, BelowZero }

/// <summary>
/// Единственная точка изменения состояния. Команды пользователя и сигналы ОС проходят
/// через одну очередь и выполняются в одном потоке, поэтому гонок между ними нет.
/// </summary>
public sealed class TrackerService : IHostedService
{
    private static readonly TimeSpan SessionEndingTimeout = TimeSpan.FromSeconds(3);

    private readonly IEventStore _store;
    private readonly IHeartbeatStore _heartbeat;
    private readonly IActivityMonitor _activity;
    private readonly ISessionEventsSource _session;
    private readonly TimeProvider _time;
    private readonly TrackerOptions _options;
    private readonly ILogger<TrackerService> _logger;

    private readonly Channel<Action> _commands = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stopping = new();

    private volatile TimerState _state = TimerState.Stop;
    private volatile IReadOnlyList<TrackerEvent> _events = [];
    private DateTimeOffset _lastTransitionAt = DateTimeOffset.MinValue;
    private Task _commandLoop = Task.CompletedTask;
    private Task _heartbeatLoop = Task.CompletedTask;

    public TrackerService(
        IEventStore store,
        IHeartbeatStore heartbeat,
        IActivityMonitor activity,
        ISessionEventsSource session,
        TimeProvider time,
        IOptions<TrackerOptions> options,
        ILogger<TrackerService> logger)
    {
        _store = store;
        _heartbeat = heartbeat;
        _activity = activity;
        _session = session;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Состояние или журнал изменились. Вызывается не в потоке UI.</summary>
    public event Action? Changed;

    public TimerState State => _state;

    public IReadOnlyList<TrackerEvent> Events => _events;

    public DaySummary GetToday()
    {
        var now = _time.GetUtcNow();
        var timeZone = _time.LocalTimeZone;
        return SessionCalculator.Calculate(_events, SessionCalculator.DayOf(now, timeZone), now, timeZone);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _events = _store.ReadAll();
        RecoverAfterCrash();
        WriteHeartbeat();

        _activity.IdleDetected += OnIdleDetected;
        _activity.InputResumed += OnInputResumed;
        _session.Locked += OnLocked;
        _session.Unlocked += OnUnlocked;
        _session.Ending += NotifySessionEnding;

        _commandLoop = Task.Run(ProcessCommandsAsync, CancellationToken.None);
        _heartbeatLoop = Task.Run(() => RunHeartbeatAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _activity.IdleDetected -= OnIdleDetected;
        _activity.InputResumed -= OnInputResumed;
        _session.Locked -= OnLocked;
        _session.Unlocked -= OnUnlocked;
        _session.Ending -= NotifySessionEnding;

        _commands.Writer.TryComplete();
        await _stopping.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_commandLoop, _heartbeatLoop).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> UserStartAsync() => Enqueue(() => Fire(TransitionReason.UserStart));

    public Task<bool> UserStopAsync() => Enqueue(() => Fire(TransitionReason.UserStop));

    public Task<bool> UserExitAsync() => Enqueue(() => Fire(TransitionReason.UserExit));

    public Task<AdjustResult> AdjustAsync(int minutes, string? comment) => Enqueue(() =>
    {
        if (minutes == 0)
            return AdjustResult.InvalidMinutes;

        var today = GetToday();
        if (today.Total + TimeSpan.FromMinutes(minutes) < TimeSpan.Zero)
            return AdjustResult.BelowZero;

        Append(new TimeAdjusted(Guid.NewGuid(), _time.GetUtcNow(), today.Day, minutes, comment));
        Changed?.Invoke();
        return AdjustResult.Applied;
    });

    /// <summary>Блокирует вызывающий поток, пока событие не записано: после возврата ОС может завершить процесс.</summary>
    public void NotifySessionEnding()
    {
        try
        {
            Enqueue(() => Fire(TransitionReason.SessionEnding)).Wait(SessionEndingTimeout);
        }
        catch (AggregateException ex)
        {
            _logger.LogError(ex, "Не удалось записать завершение сеанса");
        }
    }

    internal Task FlushAsync() => Enqueue(() => true);

    private void OnIdleDetected(DateTimeOffset lastInputAt) => Enqueue(() => Fire(TransitionReason.IdleTimeout, lastInputAt));

    private void OnInputResumed() => Enqueue(() => Fire(TransitionReason.InputResumed));

    private void OnLocked() => Enqueue(() => Fire(TransitionReason.SessionLock));

    private void OnUnlocked() => Enqueue(() => Fire(TransitionReason.SessionUnlock));

    private bool Fire(TransitionReason trigger, DateTimeOffset? at = null)
    {
        var current = _state;
        if (TimerStateMachine.Next(current, trigger) is not { } next)
        {
            _logger.LogInformation("Недопустимый переход проигнорирован: {State} + {Trigger}", current, trigger);
            return false;
        }

        var now = _time.GetUtcNow();
        var eventAt = at ?? now;

        // Момент последнего ввода может быть раньше старта таймера, а журнал должен оставаться упорядоченным.
        if (eventAt < _lastTransitionAt)
            eventAt = _lastTransitionAt;

        // Сначала запись: если она не удалась, состояние не должно разойтись с журналом.
        Append(new StateChanged(Guid.NewGuid(), eventAt, now, current, next, trigger));
        _state = next;
        _lastTransitionAt = eventAt;
        Changed?.Invoke();
        return true;
    }

    private void RecoverAfterCrash()
    {
        var last = _events.OfType<StateChanged>().LastOrDefault();
        if (last is null)
            return;

        _lastTransitionAt = last.At;

        if (TimerStateMachine.Next(last.To, TransitionReason.RecoveredAfterCrash) is not { } next)
            return;

        var now = _time.GetUtcNow();
        var at = _heartbeat.Read() ?? last.At;
        if (at < last.At)
            at = last.At;
        if (at > now)
            at = now;

        _logger.LogWarning("Предыдущий запуск завершился некорректно, время восстановлено до {At}", at);
        Append(new StateChanged(Guid.NewGuid(), at, now, last.To, next, TransitionReason.RecoveredAfterCrash));
        _lastTransitionAt = at;
    }

    private void Append(TrackerEvent trackerEvent)
    {
        _store.Append(trackerEvent);
        _events = [.. _events, trackerEvent];
    }

    private Task<T> Enqueue<T>(Func<T> command)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = _commands.Writer.TryWrite(() =>
        {
            try
            {
                completion.SetResult(command());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке команды");
                completion.SetException(ex);
            }
        });

        if (!accepted)
            completion.SetCanceled();

        return completion.Task;
    }

    private async Task ProcessCommandsAsync()
    {
        await foreach (var command in _commands.Reader.ReadAllAsync().ConfigureAwait(false))
            command();
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.HeartbeatInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                WriteHeartbeat();
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка.
        }
    }

    private void WriteHeartbeat()
    {
        try
        {
            _heartbeat.Write(_time.GetUtcNow());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Не удалось записать heartbeat");
        }
    }
}
