using SAPbobsCOM;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;

namespace MolasLubes.Infrastructure.Integrations.SapB1.Helpers;

public static class OdooUdfMapper
{
    private const int MaxErrorLength = 250;

    // =====================================================
    // COMMON STATUS HANDLER (All Documents / BP)
    // =====================================================
    private static void ApplyCommonStatus(UserFields ufs,
        string status,
        string? syncDir,
        string? errorMsg = null)
    {
        ufs.Fields.Item(OdooUdfs.Status).Value = status;
        ufs.Fields.Item(OdooUdfs.SyncDir).Value = syncDir ?? "BIDIR";
        ufs.Fields.Item(OdooUdfs.LastSync).Value = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(errorMsg))
        {
            ufs.Fields.Item(OdooUdfs.ErrorMsg).Value =
                errorMsg.Length > MaxErrorLength
                    ? errorMsg[..MaxErrorLength]
                    : errorMsg;
        }
    }

    // =====================================================
    // SALES ORDER (ORDR)
    // =====================================================
    public static void ApplySalesOrderUdfs(
        Documents doc,
        string? odooId,
        string? syncDir = "TO_SAP")
    {
        if (!string.IsNullOrWhiteSpace(odooId))
            doc.UserFields.Fields.Item(OdooUdfs.SalesOrderId).Value = odooId.Trim();

        ApplyCommonStatus(doc.UserFields, "PENDING", syncDir);
    }

    // =====================================================
    // SALES ORDER LINE (RDR1)
    // =====================================================
    public static void ApplySalesOrderLineUdfs(
        Documents doc,
        string? odooLineId)
    {
        if (!string.IsNullOrWhiteSpace(odooLineId))
            doc.Lines.UserFields.Fields
                .Item(OdooUdfs.SalesOrderLineId).Value = odooLineId.Trim();
    }

    // =====================================================
    // CUSTOMER (OCRD)
    // =====================================================
    public static void ApplyCustomerUdfs(
        BusinessPartners bp,
        string? odooPartnerId,
        string? syncDir = "TO_SAP")
    {
        if (!string.IsNullOrWhiteSpace(odooPartnerId))
            bp.UserFields.Fields.Item(OdooUdfs.PartnerId).Value = odooPartnerId.Trim();

        ApplyCommonStatus(bp.UserFields, "PENDING", syncDir);
    }

    // =====================================================
    // DELIVERY (ODLN)
    // =====================================================
    public static void ApplyDeliveryUdfs(
        Documents delivery,
        string? odooDeliveryId,
        string? syncDir = "FROM_SAP")
    {
        if (!string.IsNullOrWhiteSpace(odooDeliveryId))
            delivery.UserFields.Fields.Item(OdooUdfs.DeliveryId).Value = odooDeliveryId.Trim();

        ApplyCommonStatus(delivery.UserFields, "PENDING", syncDir);
    }

    // =====================================================
    // DELIVERY LINE (DLN1)
    // =====================================================
    public static void ApplyDeliveryLineUdfs(
        Documents delivery,
        string? moveId)
    {
        if (!string.IsNullOrWhiteSpace(moveId))
            delivery.Lines.UserFields.Fields
                .Item(OdooUdfs.DeliveryMoveId).Value = moveId.Trim();
    }

    // =====================================================
    // INVOICE (OINV)
    // =====================================================
    public static void ApplyInvoiceUdfs(
        Documents invoice,
        int sourceDeliveryDocEntry,
        string? odooInvoiceId = null,
        string? syncDir = "FROM_SAP")
    {
        // Link to delivery (idempotency anchor)
        invoice.UserFields.Fields
            .Item("U_SourceODLN").Value = sourceDeliveryDocEntry;

        if (!string.IsNullOrWhiteSpace(odooInvoiceId))
            invoice.UserFields.Fields
                .Item(OdooUdfs.InvoiceId).Value = odooInvoiceId.Trim();

        ApplyCommonStatus(invoice.UserFields, "PENDING", syncDir);
    }

    // =====================================================
    // INVOICE LINE (INV1)
    // =====================================================
    public static void ApplyInvoiceLineUdfs(
        Documents invoice,
        string? odooLineId)
    {
        if (!string.IsNullOrWhiteSpace(odooLineId))
            invoice.Lines.UserFields.Fields
                .Item(OdooUdfs.InvoiceLineId).Value = odooLineId.Trim();
    }

    // =====================================================
    // CREDIT MEMO (ORIN)
    // =====================================================
    public static void ApplyCreditMemoUdfs(
        Documents credit,
        string? odooCreditId,
        string? syncDir = "FROM_SAP")
    {
        if (!string.IsNullOrWhiteSpace(odooCreditId))
            credit.UserFields.Fields
                .Item(OdooUdfs.CreditId).Value = odooCreditId.Trim();

        ApplyCommonStatus(credit.UserFields, "PENDING", syncDir);
    }

    // =====================================================
    // PAYMENT (ORCT)
    // =====================================================
    public static void ApplyPaymentUdfs(
        Payments payment,
        int invoiceDocEntry,
        string? odooPaymentId = null,
        string? syncDir = "FROM_SAP")
    {
        // Link to invoice
        payment.UserFields.Fields
            .Item("U_SourceInvoice").Value = invoiceDocEntry;

        if (!string.IsNullOrWhiteSpace(odooPaymentId))
            payment.UserFields.Fields
                .Item(OdooUdfs.PaymentId).Value = odooPaymentId.Trim();

        ApplyCommonStatus(payment.UserFields, "PENDING", syncDir);
    }

    // =====================================================
    // MARK SYNC SUCCESS
    // =====================================================
    public static void MarkSynced(UserFields ufs, string? syncDir = null)
    {
        ApplyCommonStatus(ufs, "SYNCED", syncDir ?? "BIDIR");
    }

    // =====================================================
    // MARK ERROR
    // =====================================================
    public static void MarkError(UserFields ufs, string errorMsg, string? syncDir = null)
    {
        ApplyCommonStatus(ufs, "ERROR", syncDir ?? "BIDIR", errorMsg);
    }
}