using TimeTracker.Core;

namespace TimeTracker.Core.Tests;

public class IdleDetectorTests
{
    private const uint ThresholdMs = 15 * 60 * 1000;

    private static IdleDetector NewDetector() => new(TimeSpan.FromMilliseconds(ThresholdMs));

    [Fact]
    public void Below_threshold_gives_no_signal()
    {
        var signal = NewDetector().Evaluate(1000, 1000 + ThresholdMs - 1, out _);

        Assert.Equal(IdleSignal.None, signal);
    }

    [Fact]
    public void Idle_is_signalled_once_and_reports_elapsed_time()
    {
        var detector = NewDetector();

        Assert.Equal(IdleSignal.IdleDetected, detector.Evaluate(1000, 1000 + ThresholdMs, out var idle));
        Assert.Equal(TimeSpan.FromMilliseconds(ThresholdMs), idle);
        Assert.Equal(IdleSignal.None, detector.Evaluate(1000, 1000 + ThresholdMs + 5000, out _));
    }

    [Fact]
    public void New_input_resumes_even_if_the_poll_came_late()
    {
        var detector = NewDetector();
        detector.Evaluate(1000, 1000 + ThresholdMs, out _);

        // Ввод был давно по меркам интервала опроса, но это всё равно новый ввод.
        var signal = detector.Evaluate(1000 + ThresholdMs + 1, 1000 + ThresholdMs + 60_000, out _);

        Assert.Equal(IdleSignal.InputResumed, signal);
    }

    [Fact]
    public void Idle_can_be_detected_again_after_resume()
    {
        var detector = NewDetector();
        detector.Evaluate(1000, 1000 + ThresholdMs, out _);
        detector.Evaluate(2_000_000, 2_000_001, out _);

        Assert.Equal(IdleSignal.IdleDetected, detector.Evaluate(2_000_000, 2_000_000 + ThresholdMs, out _));
    }

    [Fact]
    public void Tick_counter_overflow_is_handled()
    {
        var lastInput = uint.MaxValue - 1000;
        var now = unchecked(lastInput + ThresholdMs);

        Assert.True(now < lastInput);
        Assert.Equal(IdleSignal.IdleDetected, NewDetector().Evaluate(lastInput, now, out var idle));
        Assert.Equal(TimeSpan.FromMilliseconds(ThresholdMs), idle);
    }

    [Fact]
    public void Input_slightly_after_now_is_not_treated_as_long_idle()
    {
        var signal = NewDetector().Evaluate(5000, 4999, out var idle);

        Assert.Equal(IdleSignal.None, signal);
        Assert.Equal(TimeSpan.Zero, idle);
    }
}
