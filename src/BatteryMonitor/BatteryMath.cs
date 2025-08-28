namespace BatteryAnalytics.Core;

public static class BatteryMath
{
    public static double? ComputeHealth(uint designedCapacity, uint fullChargedCapacity)
    {
        if (designedCapacity == 0 || fullChargedCapacity == 0) return null;
        return (double)fullChargedCapacity / designedCapacity * 100.0;
    }
}
