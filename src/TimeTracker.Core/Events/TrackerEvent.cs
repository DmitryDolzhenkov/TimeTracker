using System.Text.Json.Serialization;

namespace TimeTracker.Core.Events;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StateChanged), "state")]
[JsonDerivedType(typeof(TimeAdjusted), "adjust")]
public abstract record TrackerEvent(Guid Id, DateTimeOffset At);

public sealed record StateChanged(
    Guid Id,
    DateTimeOffset At,            // момент, к которому относится переход
    DateTimeOffset RecordedAt,    // когда реально записали (для Idle и Recovery они различаются)
    TimerState From,
    TimerState To,
    TransitionReason Reason) : TrackerEvent(Id, At);

public sealed record TimeAdjusted(
    Guid Id,
    DateTimeOffset At,
    DateOnly Day,                 // к какому дню относится корректировка
    int Minutes,                  // + или -
    string? Comment) : TrackerEvent(Id, At);
