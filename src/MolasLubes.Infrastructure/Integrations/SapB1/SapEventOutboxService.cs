using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MolasLubes.Infrastructure.Integrations.SapB1;

/// <summary>
/// Read-only consumer of SapEventOutbox in the MolasIntegration database.
/// Only touches the Status/claim columns — never alters the schema.
/// </summary>
public class SapEventOutboxService
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60)
    };

    private readonly string _connStr;
    private readonly ILogger<SapEventOutboxService> _logger;

    public SapEventOutboxService(
        IConfiguration config,
        ILogger<SapEventOutboxService> logger)
    {
        _connStr = config.GetConnectionString("MolasIntegrationDb")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:MolasIntegrationDb is not configured.");
        _logger = logger;
    }

    /// <summary>
    /// Selects and claims up to <paramref name="batchSize"/> Pending events
    /// whose ObjectType is in <paramref name="objectTypes"/>.
    /// Returns only rows whose Status was successfully flipped to 'Processing'.
    /// </summary>
    public async Task<IReadOnlyList<SapOutboxEvent>> ClaimBatchAsync(
        string[] objectTypes,
        int batchSize,
        string claimedBy,
        CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        // Build a parameterised IN list
        var paramNames = objectTypes.Select((_, i) => $"@ot{i}").ToArray();
        var inClause   = string.Join(", ", paramNames);

        var selectSql = $@"
SELECT TOP (@batchSize)
    Id, DocEntry, ObjectType, TransactionType, AttemptCount
FROM SapEventOutbox WITH (UPDLOCK, READPAST)
WHERE Status = 'Pending'
  AND ObjectType IN ({inClause})
  AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= GETUTCDATE())
ORDER BY CreatedAtUtc";

        var candidates = new List<SapOutboxEvent>();

        await using (var cmd = new SqlCommand(selectSql, conn))
        {
            cmd.Parameters.AddWithValue("@batchSize", batchSize);
            for (var i = 0; i < objectTypes.Length; i++)
                cmd.Parameters.AddWithValue(paramNames[i], objectTypes[i]);

            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                candidates.Add(new SapOutboxEvent(
                    Id:              rdr.GetInt64(0),
                    DocEntry:        rdr.IsDBNull(1) ? null : rdr.GetInt32(1),
                    ObjectType:      rdr.GetString(2),
                    TransactionType: rdr.GetString(3),
                    AttemptCount:    rdr.GetInt32(4)));
            }
        }

        if (candidates.Count == 0) return Array.Empty<SapOutboxEvent>();

        // Claim each candidate with an optimistic update (another instance
        // might have claimed between SELECT and UPDATE — skip those).
        var claimed = new List<SapOutboxEvent>();

        const string claimSql = @"
UPDATE SapEventOutbox
   SET Status       = 'Processing',
       ClaimedAtUtc = GETUTCDATE(),
       ClaimedBy    = @claimedBy,
       AttemptCount = AttemptCount + 1
WHERE Id     = @id
  AND Status = 'Pending'";

        foreach (var ev in candidates)
        {
            await using var cmd = new SqlCommand(claimSql, conn);
            cmd.Parameters.AddWithValue("@claimedBy", claimedBy);
            cmd.Parameters.AddWithValue("@id", ev.Id);

            if (await cmd.ExecuteNonQueryAsync(ct) == 1)
                claimed.Add(ev with { AttemptCount = ev.AttemptCount + 1 });
        }

        return claimed;
    }

    public async Task MarkDoneAsync(long id, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        const string sql = @"
UPDATE SapEventOutbox
   SET Status          = 'Done',
       ProcessedAtUtc  = GETUTCDATE()
WHERE Id = @id";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(
        long id,
        string error,
        int attemptCount,
        CancellationToken ct = default)
    {
        var idx   = Math.Min(attemptCount - 1, RetryDelays.Length - 1);
        var permanent = attemptCount > RetryDelays.Length;

        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        string sql;
        if (permanent)
        {
            sql = @"
UPDATE SapEventOutbox
   SET Status            = 'Failed',
       LastError         = @error,
       NextAttemptAtUtc  = NULL
WHERE Id = @id";
        }
        else
        {
            sql = @"
UPDATE SapEventOutbox
   SET Status           = 'Pending',
       LastError        = @error,
       NextAttemptAtUtc = DATEADD(second, @delaySec, GETUTCDATE())
WHERE Id = @id";
        }

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);
        if (!permanent)
            cmd.Parameters.AddWithValue("@delaySec", (int)RetryDelays[idx].TotalSeconds);

        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogWarning(
            "SapEventOutbox event {Id}: attempt {Attempt} failed — {Disposition}",
            id, attemptCount, permanent ? "permanent failure" : $"retry in {RetryDelays[idx]}");
    }
}

public record SapOutboxEvent(
    long   Id,
    int?   DocEntry,
    string ObjectType,
    string TransactionType,
    int    AttemptCount);
