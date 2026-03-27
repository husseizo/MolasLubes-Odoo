namespace MolasLubes.Application.LiquiMolyReplenishment;

public static class LiquiMolyTrendCategory
{
    /// <summary>Stock on hand > 0 but zero sales in last 90 days.</summary>
    public const string DeadStock  = "DEAD_STOCK";

    /// <summary>Days of stock cover < 7 days at current average sales rate.</summary>
    public const string LowStock   = "LOW_STOCK";

    /// <summary>Days of stock cover < 14 days AND 30-day sales volume is high.</summary>
    public const string FastMoving = "FAST_MOVING";

    /// <summary>Adequate stock cover (≥ 14 days) with healthy sales.</summary>
    public const string Normal     = "NORMAL";

    /// <summary>Some sales in period but low velocity (avg daily sales < threshold).</summary>
    public const string SlowMoving = "SLOW_MOVING";

    /// <summary>No stock and no sales — item inactive in this warehouse.</summary>
    public const string Inactive   = "INACTIVE";
}
