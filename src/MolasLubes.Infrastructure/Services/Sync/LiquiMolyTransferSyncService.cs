using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Integrations.SapB1;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

/// <summary>
/// Polls the MolasIntegration SapEventOutbox for OWTQ / OWTR events,
/// reads the full document from the MolasLubes SAP company via DI API,
/// and upserts headers + lines into the Neon LiquiMolyTransfers tables.
/// </summary>
public class LiquiMolyTransferSyncService
{
    private const int BatchSize  = 10;
    private const string ProfileKey = "MolasLubes";

    private static readonly string[] WatchedObjectTypes =
    {
        SapLiquiMolyTransferReader.OwtqObjectType,  // "67"
        SapLiquiMolyTransferReader.OwtrObjectType   // "1250000001"
    };

    private static readonly string ClaimedBy =
        $"LiquiMolyTransferSync@{Environment.MachineName}";

    private readonly SapEventOutboxService _outbox;
    private readonly SapLiquiMolyTransferReader _reader;
    private readonly NeonDbContext _neon;
    private readonly ILogger<LiquiMolyTransferSyncService> _logger;

    public LiquiMolyTransferSyncService(
        SapEventOutboxService outbox,
        SapLiquiMolyTransferReader reader,
        NeonDbContext neon,
        ILogger<LiquiMolyTransferSyncService> logger)
    {
        _outbox = outbox;
        _reader = reader;
        _neon   = neon;
        _logger = logger;
    }

    /// <summary>
    /// Claims one batch of pending outbox events, reads the SAP documents, and upserts Neon.
    /// Call this in a tight loop (it returns when the batch is exhausted).
    /// </summary>
    public async Task ProcessPendingAsync(CancellationToken ct = default)
    {
        var events = await _outbox.ClaimBatchAsync(WatchedObjectTypes, BatchSize, ClaimedBy, ct);

        if (events.Count == 0)
        {
            _logger.LogDebug("LiquiMolyTransferSync: no pending events");
            return;
        }

        _logger.LogInformation(
            "LiquiMolyTransferSync: claimed {Count} events", events.Count);

        // Map ObjectType → DocType and collect valid requests
        var requests = new List<(int DocEntry, string DocType)>();
        var skipped  = new List<SapOutboxEvent>();

        foreach (var ev in events)
        {
            var docType = MapDocType(ev.ObjectType);
            if (docType == null || ev.DocEntry == null)
            {
                _logger.LogWarning(
                    "LiquiMolyTransferSync: skipping event {Id} — ObjectType={OT} DocEntry={DE}",
                    ev.Id, ev.ObjectType, ev.DocEntry);
                skipped.Add(ev);
                continue;
            }

            requests.Add((ev.DocEntry.Value, docType));
        }

        // Mark skipped events as failed immediately (permanent — unknown type)
        foreach (var ev in skipped)
            await _outbox.MarkFailedAsync(ev.Id, "Unknown ObjectType or missing DocEntry", ev.AttemptCount + MaxRetries, ct);

        if (requests.Count == 0) return;

        // Read all documents from SAP DI API in one connection round-trip
        IReadOnlyList<SapTransferDocumentDto> docs;
        try
        {
            docs = _reader.ReadTransfers(requests);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LiquiMolyTransferSync: SAP read failed for batch");

            // Mark all claimed events as pending-retry
            foreach (var ev in events.Where(e => !skipped.Contains(e)))
                await _outbox.MarkFailedAsync(ev.Id, ex.Message, ev.AttemptCount, ct);

            return;
        }

        // Build lookup by (DocEntry, DocType) so we can report which events succeeded
        var docMap = docs.ToDictionary(d => (d.DocEntry, d.DocType));

        // Process event-by-event: upsert Neon then mark outbox
        foreach (var ev in events)
        {
            if (skipped.Contains(ev)) continue;

            var docType = MapDocType(ev.ObjectType)!;
            var key     = (ev.DocEntry!.Value, docType);

            if (!docMap.TryGetValue(key, out var doc))
            {
                _logger.LogWarning(
                    "LiquiMolyTransferSync: SAP returned no document for DocEntry={DE} DocType={DT} (event {Id})",
                    ev.DocEntry, docType, ev.Id);
                await _outbox.MarkFailedAsync(ev.Id, "Document not found in SAP", ev.AttemptCount, ct);
                continue;
            }

            try
            {
                await UpsertTransferAsync(doc, ct);
                await _outbox.MarkDoneAsync(ev.Id, ct);

                _logger.LogInformation(
                    "LiquiMolyTransferSync: synced {DocType} DocEntry={DE} DocNum={DN} Lines={L}",
                    doc.DocType, doc.DocEntry, doc.DocNum, doc.Lines.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "LiquiMolyTransferSync: Neon upsert failed for {DocType} DocEntry={DE}",
                    doc.DocType, doc.DocEntry);
                await _outbox.MarkFailedAsync(ev.Id, ex.Message, ev.AttemptCount, ct);
            }
        }
    }

    // ── Neon upsert ───────────────────────────────────────────────────────────

    private async Task UpsertTransferAsync(SapTransferDocumentDto doc, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var existing = await _neon.LiquiMolyTransfers
            .FirstOrDefaultAsync(h => h.DocEntry == doc.DocEntry && h.DocType == doc.DocType, ct);

        if (existing != null)
        {
            existing.DocNum      = doc.DocNum;
            existing.DocDate     = DateTime.SpecifyKind(doc.DocDate, DateTimeKind.Utc);
            existing.TaxDate     = doc.TaxDate.HasValue
                ? DateTime.SpecifyKind(doc.TaxDate.Value, DateTimeKind.Utc)
                : null;
            existing.DocDueDate  = doc.DocDueDate.HasValue
                ? DateTime.SpecifyKind(doc.DocDueDate.Value, DateTimeKind.Utc)
                : null;
            existing.FromWhsCode = doc.FromWhsCode;
            existing.ToWhsCode   = doc.ToWhsCode;
            existing.Comments    = doc.Comments;
            existing.DocStatus   = doc.DocStatus;
            existing.UserSign    = doc.UserSign;
            existing.DocTotal    = doc.DocTotal;
            existing.SyncedAt    = now;
        }
        else
        {
            _neon.LiquiMolyTransfers.Add(new NeonLiquiMolyTransferHeader
            {
                DocEntry      = doc.DocEntry,
                DocType       = doc.DocType,
                DocNum        = doc.DocNum,
                SourceProfile = ProfileKey,
                DocDate       = DateTime.SpecifyKind(doc.DocDate, DateTimeKind.Utc),
                TaxDate       = doc.TaxDate.HasValue
                    ? DateTime.SpecifyKind(doc.TaxDate.Value, DateTimeKind.Utc)
                    : null,
                DocDueDate    = doc.DocDueDate.HasValue
                    ? DateTime.SpecifyKind(doc.DocDueDate.Value, DateTimeKind.Utc)
                    : null,
                FromWhsCode   = doc.FromWhsCode,
                ToWhsCode     = doc.ToWhsCode,
                Comments      = doc.Comments,
                DocStatus     = doc.DocStatus,
                UserSign      = doc.UserSign,
                DocTotal      = doc.DocTotal,
                SyncedAt      = now
            });
        }

        // Replace all lines (delete + re-insert is safe; lines are append-only in SAP)
        var oldLines = await _neon.LiquiMolyTransferLines
            .Where(l => l.DocEntry == doc.DocEntry && l.DocType == doc.DocType)
            .ToListAsync(ct);
        _neon.LiquiMolyTransferLines.RemoveRange(oldLines);

        _neon.LiquiMolyTransferLines.AddRange(doc.Lines.Select(l => new NeonLiquiMolyTransferLine
        {
            DocEntry    = l.DocEntry,
            DocType     = doc.DocType,
            LineNum     = l.LineNum,
            ItemCode    = l.ItemCode,
            Description = l.Description,
            Quantity    = l.Quantity,
            OpenQty     = l.OpenQty,
            UomCode     = l.UomCode,
            FromWhsCode = l.FromWhsCode,
            ToWhsCode   = l.ToWhsCode,
            Price       = l.Price,
            LineTotal   = l.LineTotal,
            BaseType    = l.BaseType,
            BaseEntry   = l.BaseEntry,
            BaseLine    = l.BaseLine
        }));

        await _neon.SaveChangesAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? MapDocType(string objectType) => objectType switch
    {
        SapLiquiMolyTransferReader.OwtqObjectType => "OWTQ",
        SapLiquiMolyTransferReader.OwtrObjectType => "OWTR",
        _ => null
    };

    // Must exceed SapEventOutboxService.RetryDelays.Length to force permanent failure
    private const int MaxRetries = 3;
}
