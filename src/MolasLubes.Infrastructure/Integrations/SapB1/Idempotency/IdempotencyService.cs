using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Integrations.SapB1.Errors;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.Idempotency;

public class IdempotencyService
{
    private readonly SapDiApiConnection _conn;

    public IdempotencyService(SapDiApiConnection conn)
    {
        _conn = conn;
    }

    // ---------------------------------------------
    // Invoice idempotency: by U_SourceODLN
    // ---------------------------------------------
    public int? FindInvoiceBySourceDelivery(int deliveryDocEntry)
    {
        var company = _conn.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        // OINV.U_SourceODLN is your chosen idempotency column
        rs.DoQuery($@"
            SELECT TOP 1 DocEntry
            FROM OINV
            WHERE U_SourceODLN = {deliveryDocEntry}
              AND CANCELED = 'N'
            ORDER BY DocEntry DESC");

        if (rs.RecordCount == 0) return null;

        return Convert.ToInt32(rs.Fields.Item("DocEntry").Value);
    }

    // ---------------------------------------------
    // Payment idempotency: by CounterReference
    // ---------------------------------------------
    public int? FindPaymentByCounterReference(string counterRef)
    {
        var company = _conn.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        // ORCT.CounterRef is a common safe idempotency hook
        // Field names can vary by localization; if your DB uses CounterRef/CounterReference,
        // adjust below accordingly.
        rs.DoQuery($@"
            SELECT TOP 1 DocEntry
            FROM ORCT
            WHERE CounterRef = '{Escape(counterRef)}'
              AND Canceled = 'N'
            ORDER BY DocEntry DESC");

        if (rs.RecordCount == 0) return null;

        return Convert.ToInt32(rs.Fields.Item("DocEntry").Value);
    }

    private static string Escape(string v) => (v ?? "").Replace("'", "''");
}