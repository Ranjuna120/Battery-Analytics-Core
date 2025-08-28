using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Windows.Devices.Power;

namespace BatteryAnalytics.Core;

[SupportedOSPlatform("windows10.0.17763.0")]
public static class WinRtBatteryProvider
{
    public record WinRtAggregate(
        string Name,
        int ChargePercent,
        int FullChargeCapacityMah,
        int RemainingCapacityMah
    );

    public static WinRtAggregate? QueryAggregate()
    {
        try
        {
            var batt = Battery.AggregateBattery; // system aggregate
            var report = batt.GetReport();
            // Values may be null (in mWh or mWh? docs say mWh). Convert to int fallback 0.
            int? fullDesign = report.DesignCapacityInMilliwattHours;
            int? full = report.FullChargeCapacityInMilliwattHours;
            int? remaining = report.RemainingCapacityInMilliwattHours;
            double? pct = null;
            if (remaining.HasValue && full.HasValue && full.Value > 0)
                pct = remaining.Value * 100.0 / full.Value;
            return new WinRtAggregate(
                Name: "AggregateBattery",
                ChargePercent: (int)Math.Round(pct ?? 0),
                FullChargeCapacityMah: full ?? 0,
                RemainingCapacityMah: remaining ?? 0
            );
        }
        catch { return null; }
    }
}
