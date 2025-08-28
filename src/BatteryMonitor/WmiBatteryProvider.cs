using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using System.Management;

namespace BatteryAnalytics.Core;

 [SupportedOSPlatform("windows")]
public static class WmiBatteryProvider
{
    public record WmiBatterySample(
        string Name,
        string DeviceId,
        int? EstimatedChargeRemaining,
        int? EstimatedRunTimeMinutes,
        int? BatteryStatus,
        int? DesignVoltageMv,
        int? ChemistryCode
    );

    public static IEnumerable<WmiBatterySample> Query()
    {
        var list = new List<WmiBatterySample>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT * FROM Win32_Battery");
            foreach (ManagementObject mo in searcher.Get())
            {
                int? GetInt(string prop)
                {
                    try { var v = mo[prop]; if (v == null) return null; if (int.TryParse(v.ToString(), out var n)) return n; } catch { }
                    return null;
                }
                list.Add(new WmiBatterySample(
                    Name: (mo["Name"] as string) ?? "Battery",
                    DeviceId: (mo["DeviceID"] as string) ?? "Unknown",
                    EstimatedChargeRemaining: GetInt("EstimatedChargeRemaining"),
                    EstimatedRunTimeMinutes: GetInt("EstimatedRunTime"),
                    BatteryStatus: GetInt("BatteryStatus"),
                    DesignVoltageMv: GetInt("DesignVoltage"),
                    ChemistryCode: GetInt("Chemistry")
                ));
            }
        }
        catch { }
        return list;
    }
}
