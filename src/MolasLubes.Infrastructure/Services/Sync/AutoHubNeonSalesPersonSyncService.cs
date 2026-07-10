using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class AutoHubNeonSalesPersonSyncService
{
    private readonly SapAutoHubSalesPersonReader _reader;
    private readonly AutoHubDbContext _db;
    private readonly ILogger<AutoHubNeonSalesPersonSyncService> _logger;

    public AutoHubNeonSalesPersonSyncService(
        SapAutoHubSalesPersonReader reader,
        AutoHubDbContext db,
        ILogger<AutoHubNeonSalesPersonSyncService> logger)
    {
        _reader = reader;
        _db     = db;
        _logger = logger;
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("AutoHubSalesPersonSync: started");

        var rows = _reader.ReadAll();

        _logger.LogInformation("AutoHubSalesPersonSync: SAP returned {Count} salespeople", rows.Count);

        if (rows.Count == 0)
        {
            _logger.LogWarning("AutoHubSalesPersonSync: SAP returned 0 rows — aborting");
            return;
        }

        var strategy = _db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var now      = DateTime.UtcNow;
            var existing = await _db.AutoHubSalesPersons
                .ToDictionaryAsync(p => p.SalesPersonCode, ct);

            var incomingCodes = new HashSet<int>(rows.Select(r => r.SlpCode));

            int upserted = 0;

            foreach (var row in rows)
            {
                if (existing.TryGetValue(row.SlpCode, out var entity))
                {
                    entity.SalesPersonName = row.SlpName;
                    entity.IsActive        = row.IsActive;
                    entity.Email           = row.Email;
                    entity.SyncedAt        = now;
                }
                else
                {
                    _db.AutoHubSalesPersons.Add(new NeonAutoHubSalesPerson
                    {
                        SalesPersonCode = row.SlpCode,
                        SalesPersonName = row.SlpName,
                        IsActive        = row.IsActive,
                        Email           = row.Email,
                        SyncedAt        = now
                    });
                }
                upserted++;
            }

            // Mark salespeople no longer in SAP as inactive
            foreach (var entity in existing.Values.Where(e => !incomingCodes.Contains(e.SalesPersonCode)))
            {
                entity.IsActive = false;
                entity.SyncedAt = now;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("AutoHubSalesPersonSync: completed | Upserted={U}", upserted);
        });
    }
}
