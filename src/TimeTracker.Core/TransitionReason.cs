namespace TimeTracker.Core;

public enum TransitionReason
{
    UserStart, UserStop,
    IdleTimeout, InputResumed,
    SessionLock, SessionUnlock,
    SessionEnding, RecoveredAfterCrash,
    UserExit
}
