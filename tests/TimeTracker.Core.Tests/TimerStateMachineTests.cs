using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

public class TimerStateMachineTests
{
    [Theory]
    [InlineData(TimerState.Stop, TransitionReason.UserStart, TimerState.Active)]
    [InlineData(TimerState.Active, TransitionReason.UserStop, TimerState.Stop)]
    [InlineData(TimerState.Active, TransitionReason.IdleTimeout, TimerState.Idle)]
    [InlineData(TimerState.Idle, TransitionReason.InputResumed, TimerState.Active)]
    [InlineData(TimerState.Idle, TransitionReason.UserStop, TimerState.Stop)]
    [InlineData(TimerState.Active, TransitionReason.SessionLock, TimerState.Lock)]
    [InlineData(TimerState.Idle, TransitionReason.SessionLock, TimerState.Lock)]
    [InlineData(TimerState.Stop, TransitionReason.SessionLock, TimerState.Lock)]
    [InlineData(TimerState.Lock, TransitionReason.SessionUnlock, TimerState.Stop)]
    [InlineData(TimerState.Active, TransitionReason.SessionEnding, TimerState.Shutdown)]
    [InlineData(TimerState.Idle, TransitionReason.SessionEnding, TimerState.Shutdown)]
    [InlineData(TimerState.Stop, TransitionReason.SessionEnding, TimerState.Shutdown)]
    [InlineData(TimerState.Lock, TransitionReason.SessionEnding, TimerState.Shutdown)]
    [InlineData(TimerState.Active, TransitionReason.UserExit, TimerState.Shutdown)]
    [InlineData(TimerState.Idle, TransitionReason.UserExit, TimerState.Shutdown)]
    [InlineData(TimerState.Stop, TransitionReason.UserExit, TimerState.Shutdown)]
    [InlineData(TimerState.Active, TransitionReason.RecoveredAfterCrash, TimerState.Shutdown)]
    [InlineData(TimerState.Idle, TransitionReason.RecoveredAfterCrash, TimerState.Shutdown)]
    public void Valid_transitions(TimerState from, TransitionReason trigger, TimerState expected) =>
        Assert.Equal(expected, TimerStateMachine.Next(from, trigger));

    [Theory]
    [InlineData(TimerState.Lock, TransitionReason.UserStart)]
    [InlineData(TimerState.Active, TransitionReason.UserStart)]
    [InlineData(TimerState.Stop, TransitionReason.UserStop)]
    [InlineData(TimerState.Stop, TransitionReason.IdleTimeout)]
    [InlineData(TimerState.Active, TransitionReason.InputResumed)]
    [InlineData(TimerState.Lock, TransitionReason.SessionLock)]
    [InlineData(TimerState.Stop, TransitionReason.SessionUnlock)]
    [InlineData(TimerState.Stop, TransitionReason.RecoveredAfterCrash)]
    [InlineData(TimerState.Lock, TransitionReason.RecoveredAfterCrash)]
    [InlineData(TimerState.Shutdown, TransitionReason.UserStart)]
    [InlineData(TimerState.Shutdown, TransitionReason.SessionEnding)]
    [InlineData(TimerState.Shutdown, TransitionReason.UserExit)]
    public void Invalid_transitions_return_null(TimerState from, TransitionReason trigger) =>
        Assert.Null(TimerStateMachine.Next(from, trigger));
}
