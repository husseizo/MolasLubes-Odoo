using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class TantivyPartsSyncService
{
    private readonly AutoHubDbContext _db;
    private readonly SapAutoHubSeedReader _sapReader;
    private readonly ILogger<TantivyPartsSyncService> _logger;

    public TantivyPartsSyncService(
        AutoHubDbContext db,
        SapAutoHubSeedReader sapReader,
        ILogger<TantivyPartsSyncService> logger)
    {
        _db        = db;
        _sapReader = sapReader;
        _logger    = logger;
    }

    public async Task<(int upserted, int removed)> SyncAsync(CancellationToken ct = default)
    {
        var rows = _sapReader.ReadTantivyParts();

        _logger.LogInformation(
            "TantivyPartsSyncService: SAP returned {Count} rows (VIKA/BORSEHUNG/DPA)",
            rows.Count);

        var now      = DateTime.UtcNow;
        var sapCodes = rows.Select(r => r.ItemCode).ToHashSet(StringComparer.Ordinal);

        var existing = await _db.TantivyParts
            .ToDictionaryAsync(p => p.ItemCode, StringComparer.Ordinal, ct);

        foreach (var row in rows)
        {
            if (existing.TryGetValue(row.ItemCode, out var entity))
            {
                entity.ItemName   = row.ItemName;
                entity.MdlTest    = row.MdlTest;
                entity.ArticleNo  = row.ArticleNo;
                entity.EngineCode = row.EngineCode;
                entity.SyncedAt   = now;
            }
            else
            {
                _db.TantivyParts.Add(new TantivyPart
                {
                    ItemCode  = row.ItemCode,
                    ItemName  = row.ItemName,
                    MdlTest   = row.MdlTest,
                    ArticleNo = row.ArticleNo,
                    EngineCode = row.EngineCode,
                    SyncedAt  = now
                });
            }
        }

        var toRemove = existing.Values
            .Where(p => !sapCodes.Contains(p.ItemCode))
            .ToList();

        if (toRemove.Count > 0)
            _db.TantivyParts.RemoveRange(toRemove);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "TantivyPartsSyncService: upserted={Upserted} removed={Removed}",
            rows.Count, toRemove.Count);

        return (rows.Count, toRemove.Count);
    }
}
