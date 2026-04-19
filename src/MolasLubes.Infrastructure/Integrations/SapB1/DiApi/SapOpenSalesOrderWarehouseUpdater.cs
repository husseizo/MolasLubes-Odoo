using Microsoft.Extensions.Logging;
using SAPbobsCOM;
using System.Runtime.Versioning;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Reads open SAP Sales Orders (ORDR, DocStatus='O') and updates fully-open
/// line warehouses to COCWHSE.
///
/// Safety rules:
/// - Only process non-cancelled open orders.
/// - Only process lines whose status is still open.
/// - Skip lines whose RemainingOpenQuantity is lower than the original quantity,
///   because SAP has already partially delivered or otherwise copied them.
/// - Skip lines already assigned to the target warehouse.
/// </summary>
[SupportedOSPlatform("windows")]
public class SapOpenSalesOrderWarehouseUpdater
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapOpenSalesOrderWarehouseUpdater> _logger;

    private const string TargetWarehouse = "COCWHSE";

    public SapOpenSalesOrderWarehouseUpdater(
        SapDiApiConnection connection,
        ILogger<SapOpenSalesOrderWarehouseUpdater> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public Task<UpdateResult> UpdateOpenOrderWarehousesAsync()
    {
        _logger.LogInformation(
            "Reading SAP open sales orders to update warehouse to {TargetWarehouse}",
            TargetWarehouse);

        var result = new UpdateResult();

        try
        {
            var openOrderEntries = ReadOpenSalesOrderEntries();

            _logger.LogInformation(
                "Found {Count} open sales orders",
                openOrderEntries.Count);

            foreach (var docEntry in openOrderEntries)
            {
                try
                {
                    var outcome = UpdateOrderWarehouse(docEntry);

                    if (outcome == UpdateOutcome.Updated)
                        result.UpdatedCount++;
                    else if (outcome == UpdateOutcome.Skipped)
                        result.SkippedCount++;
                    else
                        result.FailedCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    _logger.LogError(ex,
                        "Failed to update warehouse for sales order DocEntry={DocEntry}",
                        docEntry);
                }
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error while updating open sales order warehouses");
            throw;
        }
    }

    private List<int> ReadOpenSalesOrderEntries()
    {
        var entries = new List<int>();
        var company = _connection.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        rs.DoQuery(@"
SELECT DocEntry
FROM ORDR
WHERE ISNULL(CANCELED, 'N') = 'N'
  AND DocStatus = 'O'
  AND U_Odoo_SO_ID IS NOT NULL
ORDER BY DocEntry
");

        while (!rs.EoF)
        {
            var docEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value);
            entries.Add(docEntry);
            rs.MoveNext();
        }

        return entries;
    }

    private UpdateOutcome UpdateOrderWarehouse(int docEntry)
    {
        var company = _connection.GetConnectedCompany();
        var order = (Documents)company.GetBusinessObject(BoObjectTypes.oOrders);

        if (!order.GetByKey(docEntry))
        {
            _logger.LogWarning(
                "Could not load sales order DocEntry={DocEntry}",
                docEntry);
            return UpdateOutcome.Skipped;
        }

        var anyLineUpdated = false;
        var editableLines = 0;
        var changedLines = 0;
        var skippedLines = 0;

        for (var lineNum = 0; lineNum < order.Lines.Count; lineNum++)
        {
            order.Lines.SetCurrentLine(lineNum);

            var lineStatus = order.Lines.LineStatus;
            var lineQuantity = order.Lines.Quantity;
            var remainingOpenQuantity = order.Lines.RemainingOpenQuantity;

            // SAP defines RemainingOpenQuantity as original quantity minus
            // delivered or credited quantity. If it is lower than the original
            // quantity, treat the line as partially processed and leave it alone.
            if (lineStatus == BoStatus.bost_Close ||
                remainingOpenQuantity <= 0 ||
                remainingOpenQuantity < lineQuantity)
            {
                skippedLines++;
                _logger.LogDebug(
                    "Skipping order line | DocEntry={DocEntry} LineNum={LineNum} Status={Status} Quantity={Quantity} RemainingOpenQuantity={RemainingOpenQuantity}",
                    docEntry,
                    lineNum,
                    lineStatus,
                    lineQuantity,
                    remainingOpenQuantity);
                continue;
            }

            editableLines++;

            var currentWarehouse = order.Lines.WarehouseCode ?? string.Empty;
            if (string.Equals(currentWarehouse, TargetWarehouse, StringComparison.OrdinalIgnoreCase))
                continue;

            order.Lines.WarehouseCode = TargetWarehouse;
            anyLineUpdated = true;
            changedLines++;

            _logger.LogDebug(
                "Updated warehouse on order line | DocEntry={DocEntry} LineNum={LineNum} From={CurrentWarehouse} To={TargetWarehouse}",
                docEntry,
                lineNum,
                currentWarehouse,
                TargetWarehouse);
        }

        if (!anyLineUpdated)
        {
            _logger.LogDebug(
                "Skipped sales order | DocEntry={DocEntry} DocNum={DocNum} EditableLines={EditableLines} SkippedLines={SkippedLines}",
                docEntry,
                order.DocNum,
                editableLines,
                skippedLines);

            return UpdateOutcome.Skipped;
        }

        var retVal = order.Update();
        if (retVal != 0)
        {
            _logger.LogError(
                "SAP update failed for sales order | DocEntry={DocEntry} DocNum={DocNum} ReturnCode={ReturnCode} Error={Error}",
                docEntry,
                order.DocNum,
                retVal,
                company.GetLastErrorDescription());

            return UpdateOutcome.Failed;
        }

        _logger.LogInformation(
            "Sales order warehouse updated | DocEntry={DocEntry} DocNum={DocNum} EditableLines={EditableLines} ChangedLines={ChangedLines} SkippedLines={SkippedLines}",
            docEntry,
            order.DocNum,
            editableLines,
            changedLines,
            skippedLines);

        return UpdateOutcome.Updated;
    }
}

public enum UpdateOutcome
{
    Updated,
    Skipped,
    Failed
}

public class UpdateResult
{
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
}
