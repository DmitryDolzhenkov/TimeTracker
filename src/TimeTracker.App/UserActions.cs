using System;
using System.Threading.Tasks;
using System.Windows;
using TimeTracker.Core;

namespace TimeTracker.App;

/// <summary>Действия, общие для главного окна и меню в трее.</summary>
internal static class UserActions
{
    public static bool CanToggle(TimerState state) => state is TimerState.Stop or TimerState.Active or TimerState.Idle;

    public static string ToggleCaption(TimerState state) => state is TimerState.Active or TimerState.Idle ? "Стоп" : "Старт";

    public static async Task ToggleAsync(TrackerService tracker)
    {
        try
        {
            if (tracker.State == TimerState.Stop)
                await tracker.UserStartAsync();
            else
                await tracker.UserStopAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    public static async Task ExitAsync(TrackerService tracker)
    {
        try
        {
            await tracker.UserExitAsync();
        }
        catch (Exception ex)
        {
            // Выйти всё равно нужно. При следующем запуске сработает восстановление по heartbeat.
            ShowError(ex);
            ((App)Application.Current).ShutdownApplication();
        }
    }

    public static void ShowError(Exception ex) =>
        MessageBox.Show($"Не удалось записать событие:\n{ex.Message}", "Time Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
}
