using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TimeTracker.Core;

namespace TimeTracker.App;

public partial class MainWindow : Window
{
    private readonly TrackerService _tracker;

    public MainWindow(TrackerService tracker)
    {
        _tracker = tracker;
        InitializeComponent();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();

        _tracker.Changed += () => Dispatcher.InvokeAsync(Refresh);
        Refresh();
    }

    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Крестик сворачивает в трей: таймер должен продолжать работать.
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void Refresh()
    {
        var state = _tracker.State;
        var today = _tracker.GetToday();

        StateText.Text = state.ToString();
        DurationText.Text = $"{(int)today.Total.TotalHours:00}:{today.Total:mm\\:ss}";
        WarningText.Visibility = today.ClampedToZero ? Visibility.Visible : Visibility.Collapsed;
        StartStopButton.Content = UserActions.ToggleCaption(state);
        StartStopButton.IsEnabled = UserActions.CanToggle(state);
    }

    private async void OnStartStopClick(object sender, RoutedEventArgs e) => await UserActions.ToggleAsync(_tracker);

    private async void OnAddClick(object sender, RoutedEventArgs e) => await AdjustAsync(+1);

    private async void OnSubtractClick(object sender, RoutedEventArgs e) => await AdjustAsync(-1);

    private void OnLogClick(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowLog();

    private async Task AdjustAsync(int sign)
    {
        if (!int.TryParse(MinutesBox.Text, out var minutes) || minutes <= 0)
        {
            ShowAdjustError("Введите целое положительное число минут.");
            return;
        }

        try
        {
            var comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();
            switch (await _tracker.AdjustAsync(sign * minutes, comment))
            {
                case AdjustResult.Applied:
                    MinutesBox.Clear();
                    CommentBox.Clear();
                    ShowAdjustError(null);
                    break;
                case AdjustResult.BelowZero:
                    ShowAdjustError("Итог за день не может быть меньше нуля.");
                    break;
                case AdjustResult.InvalidMinutes:
                    ShowAdjustError("Введите целое положительное число минут.");
                    break;
            }
        }
        catch (Exception ex)
        {
            UserActions.ShowError(ex);
        }
    }

    private void ShowAdjustError(string? message)
    {
        AdjustError.Text = message;
        AdjustError.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
