using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using BatteryAnalytics.Core;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Collections.Generic;

namespace BatteryMonitor.UI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<BatteryViewModel> _batteries = new();
    private readonly DispatcherTimer _timer = new();
    private BatteryInterop.BatteryDevice[] _nativeDevices = Array.Empty<BatteryInterop.BatteryDevice>();
    private bool _nativeTried;
    private TrayIconManager? _tray;
    private readonly List<(DateTime ts, double pct)> _history = new();
    private readonly TimeSpan _historyWindow = TimeSpan.FromMinutes(5);
    private const double AlertHighTemp = 50.0; // simple inline threshold
    private const double AlertLowHealth = 80.0;
    private bool _lastAlertShown;

    public ObservableCollection<BatteryViewModel> Batteries => _batteries;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _timer.Tick += TimerOnTick;
    Loaded += (_, _) => _tray = new TrayIconManager(this);
    StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
    Closing += (_, e) => { _tray?.Dispose(); };
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out var sec) || sec < 1) sec = 5;
        _timer.Interval = TimeSpan.FromSeconds(sec);
        RefreshData();
    _timer.Start();
    StatusInline.Text = "Running";
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
    _timer.Stop();
    StatusInline.Text = "Stopped";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshData();
    }

    // Tray menu helpers
    internal void StartFromTray() => Start_Click(this, new RoutedEventArgs());
    internal void StopFromTray() => Stop_Click(this, new RoutedEventArgs());
    internal void RefreshForTray() => RefreshData();

    private void TimerOnTick(object? sender, EventArgs e) => RefreshData();

    private void RefreshData()
    {
        try
        {
            if (!_nativeTried)
            {
                _nativeDevices = BatteryInterop.EnumerateBatteries(debug: false);
                _nativeTried = true;
            }
            _batteries.Clear();
            if (_nativeDevices.Length > 0)
            {
                foreach (var dev in _nativeDevices)
                {
                    try
                    {
                        var info = dev.GetStaticInfo();
                        var status = dev.GetStatus();
                        var temp = dev.GetTemperatureCelsius();
                        var health = BatteryMath.ComputeHealth(info.DesignedCapacity, info.FullChargedCapacity);
                        var vm = new BatteryViewModel
                        {
                            Source = "Native",
                            Name = info.DeviceName ?? dev.DevicePath,
                            ChargePercent = status.RemainingCapacity > 0 && info.FullChargedCapacity > 0 ? Math.Round(status.RemainingCapacity * 100.0 / info.FullChargedCapacity,1) : (double?)null,
                            RemainingCapacity = status.RemainingCapacity,
                            FullChargeCapacity = info.FullChargedCapacity,
                            HealthPct = health,
                            Voltage = status.Voltage,
                            Rate = status.Rate,
                            Temperature = temp
                        };
                        _batteries.Add(vm);
                    }
                    catch { }
                }
            }
            else
            {
                // WinRT aggregate
                var agg = WinRtBatteryProvider.QueryAggregate();
                if (agg != null)
                {
                    var vm = new BatteryViewModel
                    {
                        Source = "WinRT",
                        Name = agg.Name,
                        ChargePercent = agg.ChargePercent,
                        RemainingCapacity = (uint)agg.RemainingCapacityMah,
                        FullChargeCapacity = (uint)agg.FullChargeCapacityMah,
                        HealthPct = null,
                        Voltage = 0,
                        Rate = 0,
                        Temperature = null
                    };
                    _batteries.Add(vm);
                }
                // WMI fallback(s)
                foreach (var wb in WmiBatteryProvider.Query())
                {
                    var vm = new BatteryViewModel
                    {
                        Source = "WMI",
                        Name = wb.Name,
                        ChargePercent = wb.EstimatedChargeRemaining,
                        RemainingCapacity = (uint)(wb.EstimatedChargeRemaining ?? 0),
                        FullChargeCapacity = 0,
                        HealthPct = null,
                        Voltage = (uint)(wb.DesignVoltageMv ?? 0),
                        Rate = 0,
                        Temperature = null
                    };
                    _batteries.Add(vm);
                }
            }
            StatusInline.Text = _batteries.Count == 0 ? "No battery" : $"{_batteries.Count} device(s)";
            UpdateTrayIcon();
            UpdateHistory();
            DrawHistory();
            CheckAlerts();
        }
        catch (Exception ex)
        {
            StatusInline.Text = "Error";
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateTrayIcon()
    {
        if (_tray == null) return;
        // pick first with percent
    var pct = _batteries.Select(b => b.ChargePercent).FirstOrDefault(p => p.HasValue);
        string? state = null;
        // quick heuristic: rate sign if available
        var first = _batteries.FirstOrDefault(b => b.Rate != 0);
        if (first != null && first.Rate != 0)
            state = first.Rate < 0 ? "Discharging" : "Charging";
        _tray.Update(pct.HasValue ? (int)Math.Round(pct.Value) : (int?)null, state);
    }

    private void UpdateHistory()
    {
        double? pct = _batteries.Select(b => b.ChargePercent).FirstOrDefault(p => p.HasValue);
        if (!pct.HasValue) return;
        var now = DateTime.UtcNow;
        _history.Add((now, pct.Value));
        _history.RemoveAll(p => now - p.ts > _historyWindow);
    }

    private void DrawHistory()
    {
        if (HistoryCanvas == null) return;
        var canvas = HistoryCanvas;
        canvas.Children.Clear();
        if (_history.Count < 2) return;
        double w = canvas.ActualWidth; if (w <= 0) w = canvas.Width = 400;
        double h = canvas.ActualHeight; if (h <= 0) h = canvas.Height = 120;
        var now = DateTime.UtcNow;
        var pts = _history.OrderBy(p => p.ts).ToList();
        double totalSec = _historyWindow.TotalSeconds;
        Point? last = null;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i=0;i<pts.Count;i++)
            {
                var (ts,pct) = pts[i];
                var x = w - (now - ts).TotalSeconds / totalSec * w;
                var y = h - (pct/100.0) * h;
                var pt = new Point(x,y);
                if (i==0) ctx.BeginFigure(pt, false, false);
                else ctx.LineTo(pt, true, false);
                last = pt;
            }
        }
        geo.Freeze();
        canvas.Children.Add(new Path{ Data = geo, StrokeThickness=2, Stroke = new SolidColorBrush(Color.FromRgb(0,150,255))});
        // axes
        canvas.Children.Add(new Line{ X1=0, Y1=h, X2=w, Y2=h, Stroke=Brushes.Gray, StrokeThickness=1, StrokeDashArray=new DoubleCollection{2,4}});
        canvas.Children.Add(new Line{ X1=0, Y1=0, X2=0, Y2=h, Stroke=Brushes.Gray, StrokeThickness=1, StrokeDashArray=new DoubleCollection{2,4}});
    }

    private void CheckAlerts()
    {
        var highTemp = _batteries.Where(b => b.Temperature.HasValue).Select(b => b.Temperature!.Value).DefaultIfEmpty().Max();
        var lowHealth = _batteries.Where(b => b.HealthPct.HasValue).Select(b => b.HealthPct!.Value).DefaultIfEmpty(100).Min();
        if (!_lastAlertShown && (highTemp >= AlertHighTemp || lowHealth <= AlertLowHealth))
        {
            try
            {
                var text = highTemp >= AlertHighTemp ? $"High temperature {highTemp:F1}C" : $"Low health {lowHealth:F1}%";
                System.Windows.Forms.ToastNotificationHelper.ShowToast("Battery Alert", text);
            }
            catch { }
            _lastAlertShown = true;
        }
        if (highTemp < AlertHighTemp && lowHealth > AlertLowHealth)
            _lastAlertShown = false; // reset
    }

    private void DarkModeToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        bool dark = DarkModeToggle.IsChecked == true;
        var app = Application.Current;
        void Set(string key, Color c)
        {
            if (app.Resources[key] is Color)
                app.Resources[key] = c;
            else app.Resources[key] = c;
        }
        if (dark)
        {
            Set("BgColor", Color.FromRgb(0x1e,0x1e,0x1e));
            Set("FgColor", Colors.White);
            ChartHost.Background = new SolidColorBrush(Color.FromRgb(30,30,30));
        }
        else
        {
            Set("BgColor", Colors.White);
            Set("FgColor", Color.FromRgb(0x11,0x11,0x11));
            ChartHost.Background = new SolidColorBrush(Color.FromRgb(0xF5,0xF5,0xF5));
        }
    }
}

public class BatteryViewModel : INotifyPropertyChanged
{
    private string? _source; public string? Source { get => _source; set => Set(ref _source, value); }
    private string? _name; public string? Name { get => _name; set => Set(ref _name, value); }
    private double? _chargePercent; public double? ChargePercent { get => _chargePercent; set => Set(ref _chargePercent, value); }
    private uint _remaining; public uint RemainingCapacity { get => _remaining; set => Set(ref _remaining, value); }
    private uint _full; public uint FullChargeCapacity { get => _full; set => Set(ref _full, value); }
    private double? _health; public double? HealthPct { get => _health; set => Set(ref _health, value); }
    private uint _voltage; public uint Voltage { get => _voltage; set => Set(ref _voltage, value); }
    private int _rate; public int Rate { get => _rate; set => Set(ref _rate, value); }
    private double? _temp; public double? Temperature { get => _temp; set => Set(ref _temp, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name=null)
    {
        if (!Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
