using System;
using System.Linq;
using System.Windows;
using TimeTracker.Core;
using TimeTracker.Core.Events;

namespace TimeTracker.App;

public partial class LogWindow : Window
{
    private readonly TrackerService _tracker;

    public LogWindow(TrackerService tracker)
    {
        _tracker = tracker;
        InitializeComponent();

        _tracker.Changed += OnChanged;
        Closed += (_, _) => _tracker.Changed -= OnChanged;
        Refresh();
    }

    private void OnChanged() => Dispatcher.InvokeAsync(Refresh);

    private void Refresh() => Entries.ItemsSource = _tracker.Events.Reverse().Select(ToEntry).ToList();

    private static LogEntry ToEntry(TrackerEvent trackerEvent)
    {
        var time = trackerEvent.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        return trackerEvent switch
        {
            StateChanged change => new LogEntry(time, $"{change.From} → {change.To}", change.Reason.ToString()),
            TimeAdjusted adjustment => new LogEntry(
                time,
                $"Корректировка {adjustment.Minutes:+0;-0} мин",
                $"за {adjustment.Day:yyyy-MM-dd}" + (adjustment.Comment is null ? "" : $": {adjustment.Comment}")),
            _ => new LogEntry(time, trackerEvent.GetType().Name, "")
        };
    }

    private sealed record LogEntry(string Time, string Title, string Details);
}
