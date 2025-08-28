using BatteryAnalytics.Core;
using System.Globalization;
using System.Net;
using System.Text;
using static System.Console;

// Simple CLI + logging + alerts + optional HTTP metrics exporter

var options = CliOptions.Parse(Environment.GetCommandLineArgs());

WriteLine("Battery Analytics Core - Low-level Battery Monitor\n");
if (options.ShowHelp)
{
    WriteLine(CliOptions.HelpText);
    return;
}

BatteryInterop.BatteryDevice[] batteries;
try
{
    batteries = BatteryInterop.EnumerateBatteries();
}
catch (Exception ex)
{
    WriteLine($"Enumeration error: {ex.Message}");
    return;
}

if (batteries.Length == 0)
{
    WriteLine("No batteries found.");
    return;
}

var staticInfos = new List<(BatteryInterop.BatteryDevice dev, BatteryInterop.BatteryStaticInfo info, double? health, double? temperature)>();
foreach (var b in batteries)
{
    try
    {
        var info = b.GetStaticInfo();
    double? health = BatteryMath.ComputeHealth(info.DesignedCapacity, info.FullChargedCapacity);
        var temp = b.GetTemperatureCelsius();
        staticInfos.Add((b, info, health, temp));
    }
    catch (Exception ex)
    {
        WriteLine($"Error reading static info: {ex.Message}");
    }
}

foreach (var tuple in staticInfos)
{
    var (dev, info, health, temp) = tuple;
    WriteLine($"Device: {info.DevicePath}");
    WriteLine($"  Manufacturer : {info.Manufacturer ?? "<n/a>"}");
    WriteLine($"  Device Name  : {info.DeviceName ?? "<n/a>"}");
    WriteLine($"  Serial       : {info.SerialNumber ?? "<n/a>"}");
    WriteLine($"  Chemistry    : {info.Chemistry ?? "<n/a>"}");
    WriteLine($"  Designed Cap : {info.DesignedCapacity} mWh");
    WriteLine($"  Full Chg Cap : {info.FullChargedCapacity} mWh");
    WriteLine($"  Cycle Count  : {info.CycleCount?.ToString() ?? "<n/a>"}");
    WriteLine($"  Mfg Date     : {info.ManufactureDate?.ToString() ?? "<n/a>"}");
    WriteLine($"  Health       : {(health.HasValue ? health.Value.ToString("F1") + "%" : "<n/a>")}");
    WriteLine($"  Temperature  : {(temp.HasValue ? temp.Value.ToString("F1") + " °C" : "<n/a>")}");
    WriteLine();
}

if (options.Once)
    return;

CsvLogger? csv = null;
if (!string.IsNullOrWhiteSpace(options.CsvPath))
{
    try
    {
        csv = new CsvLogger(options.CsvPath!);
        csv.WriteHeaderIfNew();
        WriteLine($"CSV logging: {options.CsvPath}");
    }
    catch (Exception ex)
    {
        WriteLine($"CSV init failed: {ex.Message}");
    }
}

MetricsServer? metricsServer = null;
if (options.HttpPort.HasValue)
{
    try
    {
        metricsServer = new MetricsServer(options.HttpPort.Value);
        metricsServer.Start();
        WriteLine($"Metrics HTTP endpoint: http://localhost:{options.HttpPort}/metrics");
    }
    catch (Exception ex)
    {
        WriteLine($"Metrics server failed: {ex.Message}");
    }
}

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

WriteLine($"Polling live status every {options.IntervalSeconds} s (Ctrl+C to exit) ...\n");

var alertState = new AlertState();
int cycle = 0;
while (!cts.IsCancellationRequested)
{
    var now = DateTime.UtcNow;
    double totalRemaining = 0;
    double totalFull = 0;
    foreach (var b in batteries)
    {
        try
        {
            var st = b.GetStatus();
            var staticInfo = staticInfos.First(x => x.dev == b).info;
            var health = BatteryMath.ComputeHealth(staticInfo.DesignedCapacity, staticInfo.FullChargedCapacity);
            totalRemaining += st.RemainingCapacity;
            totalFull += staticInfo.FullChargedCapacity;

            WriteLine($"[{st.TimestampUtc:HH:mm:ss}] {staticInfo.DeviceName ?? st.DevicePath}");
            WriteLine($"  Remaining : {st.RemainingCapacity} mWh");
            WriteLine($"  Rate      : {st.Rate} mW");
            WriteLine($"  Voltage   : {st.Voltage} mV");
            WriteLine($"  State     : {st.PowerState}");
            if (health.HasValue)
                WriteLine($"  Health    : {health.Value:F1}%");
            var temp = b.GetTemperatureCelsius();
            if (temp.HasValue)
                WriteLine($"  Temp      : {temp.Value:F1} °C");
            var etaSec = st.EstimatedSecondsToEmpty;
            if (etaSec.HasValue)
            {
                var ts = TimeSpan.FromSeconds(etaSec.Value);
                WriteLine($"  Est Empty : {ts:hh\\:mm\\:ss}");
            }
            WriteLine();

            csv?.WriteSample(now, staticInfo, st, temp, health);
            metricsServer?.UpdateSample(staticInfo, st, temp, health);
            AlertLogic.Check(alertState, staticInfo, st, temp, health, options);
        }
        catch (Exception ex)
        {
            WriteLine($"Status error: {ex.Message}");
        }
    }

    if (totalFull > 0)
    {
        var aggPct = (totalRemaining / totalFull) * 100.0;
        WriteLine($"Aggregate Remaining: {totalRemaining:F0} / {totalFull:F0} mWh ({aggPct:F1}% of full charge)\n");
    }

    cycle++;
    if (options.ReenumerateEvery > 0 && cycle % options.ReenumerateEvery == 0)
    {
        try
        {
            var newDevices = BatteryInterop.EnumerateBatteries();
            // merge: replace arrays & static info list
            batteries = newDevices;
            staticInfos.Clear();
            foreach (var b in batteries)
            {
                try
                {
                    var info = b.GetStaticInfo();
                    var h = BatteryMath.ComputeHealth(info.DesignedCapacity, info.FullChargedCapacity);
                    var t = b.GetTemperatureCelsius();
                    staticInfos.Add((b, info, h, t));
                }
                catch (Exception ex)
                {
                    WriteLine($"Re-enum static info error: {ex.Message}");
                }
            }
            WriteLine("[Re-enumerated battery devices]");
        }
        catch (Exception ex)
        {
            WriteLine($"Re-enumeration failed: {ex.Message}");
        }
    }

    if (cts.IsCancellationRequested) break;
    for (int i = 0; i < options.IntervalSeconds * 10 && !cts.IsCancellationRequested; i++)
        Thread.Sleep(100); // responsive cancel
}

csv?.Dispose();
metricsServer?.Dispose();

// -------- Helper classes --------

record CliOptions(
    bool ShowHelp,
    int IntervalSeconds,
    string? CsvPath,
    int? HttpPort,
    bool Once,
    double AlertHighTempC,
    double AlertLowHealthPct,
    int ReenumerateEvery)
{
    public static string HelpText => @"Options:
  --interval <sec>       Polling interval seconds (default 5)
  --csv <path>           Append CSV logging
  --http <port>          Expose /metrics for Prometheus
  --once                 Output static info only (no polling)
  --high-temp <C>        Alert threshold temperature (default 50)
  --low-health <pct>     Alert threshold health percent (default 80)
    --reenum <n>           Re-enumerate batteries every n polling cycles (0=never)
    --help                 Show this help";

    public static CliOptions Parse(string[] args)
    {
        int interval = 5;
        string? csv = null;
        int? http = null;
        bool once = false;
        double highTemp = 50;
        double lowHealth = 80;
    bool help = false;
    int reenum = 0;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--interval" when i + 1 < args.Length && int.TryParse(args[i + 1], out var iv): interval = Math.Max(1, iv); i++; break;
                case "--csv" when i + 1 < args.Length: csv = args[++i]; break;
                case "--http" when i + 1 < args.Length && int.TryParse(args[i + 1], out var hp): http = hp; i++; break;
                case "--once": once = true; break;
                case "--high-temp" when i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ht): highTemp = ht; i++; break;
                case "--low-health" when i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lh): lowHealth = lh; i++; break;
                case "--reenum" when i + 1 < args.Length && int.TryParse(args[i + 1], out var rn): reenum = Math.Max(0, rn); i++; break;
                case "--help": help = true; break;
            }
        }
    return new CliOptions(help, interval, csv, http, once, highTemp, lowHealth, reenum);
    }
}

sealed class CsvLogger : IDisposable
{
    private readonly string _path;
    private readonly object _lock = new();
    private bool _headerWritten;

    public CsvLogger(string path) { _path = Path.GetFullPath(path); }
    public void WriteHeaderIfNew()
    {
        if (File.Exists(_path)) { _headerWritten = true; return; }
        lock (_lock)
        {
            File.AppendAllText(_path, "TimestampUtc,Device,Remaining_mWh,Rate_mW,Voltage_mV,Temperature_C,Health_pct,Designed_mWh,Full_mWh,CycleCount\n");
            _headerWritten = true;
        }
    }

    public void WriteSample(DateTime timestampUtc, BatteryInterop.BatteryStaticInfo info, BatteryInterop.BatteryDynamicStatus st, double? temp, double? health)
    {
        if (!_headerWritten) WriteHeaderIfNew();
        var line = string.Join(',', new string[] {
            timestampUtc.ToString("o"),
            Escape(info.DeviceName ?? info.DevicePath),
            st.RemainingCapacity.ToString(),
            st.Rate.ToString(),
            st.Voltage.ToString(),
            temp?.ToString("F2", CultureInfo.InvariantCulture) ?? "",
            health?.ToString("F2", CultureInfo.InvariantCulture) ?? "",
            info.DesignedCapacity.ToString(),
            info.FullChargedCapacity.ToString(),
            info.CycleCount?.ToString() ?? ""
        });
        lock (_lock)
            File.AppendAllText(_path, line + Environment.NewLine);
    }

    private static string Escape(string s)
    {
        if (!s.Contains(',')) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\""; // double quotes inside
    }
    public void Dispose() { }
}

sealed class MetricsServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly object _lock = new();
    private readonly Dictionary<string,string> _metrics = new();
    private readonly Thread _thread;
    private volatile bool _running = true;
    public int Port { get; }
    public MetricsServer(int port)
    {
        Port = port;
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _thread = new Thread(Loop) { IsBackground = true };
    }
    public void Start() { _listener.Start(); _thread.Start(); }
    public void UpdateSample(BatteryInterop.BatteryStaticInfo info, BatteryInterop.BatteryDynamicStatus st, double? temp, double? health)
    {
        var dev = info.DeviceName ?? info.DevicePath;
        string label = dev.Replace('"','_');
        lock (_lock)
        {
            _metrics[$"battery_remaining_mwh{{device=\"{label}\"}}"] = st.RemainingCapacity.ToString(CultureInfo.InvariantCulture);
            _metrics[$"battery_rate_mw{{device=\"{label}\"}}"] = st.Rate.ToString(CultureInfo.InvariantCulture);
            _metrics[$"battery_voltage_mv{{device=\"{label}\"}}"] = st.Voltage.ToString(CultureInfo.InvariantCulture);
            if (temp.HasValue) _metrics[$"battery_temperature_c{{device=\"{label}\"}}"] = temp.Value.ToString("F2", CultureInfo.InvariantCulture);
            if (health.HasValue) _metrics[$"battery_health_pct{{device=\"{label}\"}}"] = health.Value.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
    private void Loop()
    {
        while (_running)
        {
            try
            {
                var ctx = _listener.GetContext();
                if (ctx.Request.Url?.AbsolutePath == "/metrics")
                {
                    string body;
                    lock (_lock)
                        body = string.Join('\n', _metrics.Select(kv => kv.Key + " " + kv.Value)) + "\n";
                    var buf = Encoding.UTF8.GetBytes(body);
                    ctx.Response.ContentType = "text/plain; version=0.0.4";
                    ctx.Response.OutputStream.Write(buf,0,buf.Length);
                    ctx.Response.Close();
                }
                else
                {
                    ctx.Response.StatusCode = 404; ctx.Response.Close();
                }
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch { }
        }
    }
    public void Dispose()
    {
        _running = false;
        if (_listener.IsListening) _listener.Close();
    }
}

static class AlertLogic
{
    public static void Check(AlertState state, BatteryInterop.BatteryStaticInfo info, BatteryInterop.BatteryDynamicStatus st, double? temp, double? health, CliOptions opts)
    {
        if (temp.HasValue && temp.Value >= opts.AlertHighTempC)
        {
            if (state.RecordTempAlert(info.DevicePath, temp.Value))
                WriteLine($"ALERT: High temperature {temp.Value:F1} °C (>= {opts.AlertHighTempC}) on {info.DeviceName ?? info.DevicePath}");
        }
        if (health.HasValue && health.Value <= opts.AlertLowHealthPct)
        {
            if (state.RecordHealthAlert(info.DevicePath, health.Value))
                WriteLine($"ALERT: Low health {health.Value:F1}% (<= {opts.AlertLowHealthPct}%) on {info.DeviceName ?? info.DevicePath}");
        }
    }
}

sealed class AlertState
{
    private readonly Dictionary<string,double> _lastTempAlert = new();
    private readonly Dictionary<string,double> _lastHealthAlert = new();
    public bool RecordTempAlert(string dev, double temp)
    {
        if (_lastTempAlert.TryGetValue(dev, out var prev) && Math.Abs(prev - temp) < 1) return false;
        _lastTempAlert[dev] = temp; return true;
    }
    public bool RecordHealthAlert(string dev, double health)
    {
        if (_lastHealthAlert.TryGetValue(dev, out var prev) && Math.Abs(prev - health) < 0.5) return false;
        _lastHealthAlert[dev] = health; return true;
    }
}

