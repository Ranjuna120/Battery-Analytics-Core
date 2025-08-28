using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace BatteryMonitor.UI;

internal sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly MainWindow _window;
    private int _lastPercent = -1;
    private string? _lastState;

    public TrayIconManager(MainWindow window)
    {
        _window = window;
        _icon = new NotifyIcon
        {
            Text = "Battery Monitor",
            Visible = true,
            Icon = SystemIcons.Application
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_,_) => ShowWindow());
        menu.Items.Add("Refresh", null, (_,_) => _window.Dispatcher.Invoke(() => _window.RefreshForTray()));
        menu.Items.Add("Start", null, (_,_) => _window.Dispatcher.Invoke(_window.StartFromTray));
        menu.Items.Add("Stop", null, (_,_) => _window.Dispatcher.Invoke(_window.StopFromTray));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_,_) => Application.Current.Shutdown());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Show();
        _window.Activate();
    }

    public void Update(int? percent, string? state)
    {
        bool changed = false;
        if (percent.HasValue && percent.Value != _lastPercent) { _lastPercent = percent.Value; changed = true; }
        if (state != null && state != _lastState) { _lastState = state; changed = true; }
        if (!changed) return;
        string text = percent.HasValue ? $"Battery {percent.Value}%" : "Battery";
        if (!string.IsNullOrWhiteSpace(state)) text += $" ({state})";
        _icon.Text = text.Length > 63 ? text.Substring(0,63) : text;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}