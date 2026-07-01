using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class AutoHubNeonStockSyncService
{
    private readonly SapAutoHubStockReader _reader;
    private readonly NeonDbContext _neon;
    private readonly ILogger<AutoHubNeonStockSyncService> _logger;

    public AutoHubNeonStockSyncService(
        SapAutoHubStockReader reader,
        NeonDbContext neon,
        ILogger<AutoHubNeonStockSyncService> logger)
    {
        _reader = reader;
        _neon   = neon;
        _logger = logger;
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("AutoHubNeonStockSync: started");

        var rows = _reader.ReadAll();

        _logger.LogInformation("AutoHubNeonStockSync: SAP returned {Count} items", rows.Count);

        if (rows.Count == 0)
        {
            _logger.LogWarning(
                "AutoHubNeonStockSync: SAP returned 0 rows — aborting to avoid wiping Neon");
            return;
        }

        var strategy = _neon.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neon.Database.BeginTransactionAsync(ct);

            var now = DateTime.UtcNow;

            var existing = await _neon.AutoHubProducts
                .ToDictionaryAsync(p => p.ItemCode, ct);

            var incomingCodes = new HashSet<string>(
                rows.Select(r => r.ItemCode), StringComparer.OrdinalIgnoreCase);

            int upserted = 0, deactivated = 0;

            foreach (var row in rows)
            {
                if (existing.TryGetValue(row.ItemCode, out var entity))
                {
                    entity.ItemName       = row.ItemName;
                    entity.OnHandSap      = row.OnHand;
                    entity.AvailableCache = Math.Max(0m, row.Available);
                    entity.IsActive       = row.OnHand > 0 || row.Available > 0;
                    entity.SyncedAt       = now;
                }
                else
                {
                    _neon.AutoHubProducts.Add(new NeonAutoHubProduct
                    {
                        ItemCode      = row.ItemCode,
                        ItemName      = row.ItemName,
                        OnHandSap     = row.OnHand,
                        AvailableCache = Math.Max(0m, row.Available),
                        IsActive      = row.OnHand > 0 || row.Available > 0,
                        SyncedAt      = now
                    });
                }
                upserted++;
            }

            foreach (var entity in existing.Values.Where(e => !incomingCodes.Contains(e.ItemCode)))
            {
                entity.IsActive = false;
                entity.SyncedAt = now;
                deactivated++;
            }

            await _neon.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "AutoHubNeonStockSync: completed | Upserted={U} | Deactivated={D}",
                upserted, deactivated);
        });
    }
}
