namespace MolasLubes.Infrastructure.Services.LiquiMolyTransfers;

/// <summary>
/// Generates unique transfer reference strings in the format TRF-YYYYMMDD-NNNNNN.
/// Thread-safe via interlocked counter seeded from the current UTC timestamp.
/// </summary>
public class TransferRefGenerator
{
    private static int _counter = 0;

    public string Generate()
    {
        var n = Interlocked.Increment(ref _counter);
        return $"TRF-{DateTime.UtcNow:yyyyMMdd}-{n:D6}";
    }
}
