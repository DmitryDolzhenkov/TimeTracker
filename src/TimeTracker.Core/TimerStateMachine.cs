namespace TimeTracker.Core;

public static class TimerStateMachine
{
    /// <summary>Возвращает новое состояние или null, если переход недопустим.</summary>
    public static TimerState? Next(TimerState from, TransitionReason trigger) => (from, trigger) switch
    {
        (TimerState.Stop, TransitionReason.UserStart) => TimerState.Active,
        (TimerState.Active, TransitionReason.UserStop) => TimerState.Stop,
        (TimerState.Active, TransitionReason.IdleTimeout) => TimerState.Idle,
        (TimerState.Idle, TransitionReason.InputResumed) => TimerState.Active,
        (TimerState.Idle, TransitionReason.UserStop) => TimerState.Stop,
        (TimerState.Active or TimerState.Idle or TimerState.Stop, TransitionReason.SessionLock) => TimerState.Lock,
        (TimerState.Lock, TransitionReason.SessionUnlock) => TimerState.Stop,
        (TimerState.Active or TimerState.Idle, TransitionReason.RecoveredAfterCrash) => TimerState.Shutdown,
        (not TimerState.Shutdown, TransitionReason.SessionEnding or TransitionReason.UserExit) => TimerState.Shutdown,
        _ => null
    };
}
