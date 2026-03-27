namespace MolasLubes.Infrastructure.Services.LiquiMolyReplenishment;

/// <summary>
/// Thread-safe generator for replenishment request references.
/// Format: RPL-YYYYMMDD-NNNNNN (e.g. RPL-20260326-000001)
/// Counter resets each day (day boundary detected by comparing date prefix).
/// </summary>
public class ReplenishmentRefGenerator
{
    private int    _counter;
    private string _lastDate = string.Empty;
    private readonly object _lock = new();

    public string Generate()
    {
        lock (_lock)
        {
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            if (today != _lastDate)
            {
                _counter  = 0;
                _lastDate = today;
            }

            var seq = Interlocked.Increment(ref _counter);
            return $"RPL-{today}-{seq:D6}";
        }
    }
}
