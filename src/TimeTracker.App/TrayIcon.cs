using System;
using System.Drawing;
using System.Windows;
using TimeTracker.Core;
using WinForms = System.Windows.Forms;

namespace TimeTracker.App;

internal sealed class TrayIcon : IDisposable
{
    private readonly TrackerService _tracker;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _startStop;

    public TrayIcon(TrackerService tracker, Action showMain)
    {
        _tracker = tracker;
        _startStop = new WinForms.ToolStripMenuItem("Старт", null, async (_, _) => await UserActions.ToggleAsync(_tracker));

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => showMain());
        menu.Items.Add(_startStop);
        menu.Items.Add("Лог", null, (_, _) => ((App)Application.Current).ShowLog());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Выход", null, async (_, _) => await UserActions.ExitAsync(_tracker));

        _icon = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => showMain();

        _tracker.Changed += OnChanged;
        Refresh();
    }

    public void Dispose()
    {
        _tracker.Changed -= OnChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void OnChanged() => Application.Current.Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        var state = _tracker.State;
        _icon.Text = $"Time Tracker: {state}";
        _startStop.Text = UserActions.ToggleCaption(state);
        _startStop.Enabled = UserActions.CanToggle(state);
    }
}
