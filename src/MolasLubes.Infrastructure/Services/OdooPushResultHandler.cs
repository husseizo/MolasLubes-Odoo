using SAPbobsCOM;
using MolasLubes.Infrastructure.Integrations.SapB1.Helpers;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class OdooPushResultHandler
{
    private readonly SapDiApiConnection _connection;

    public OdooPushResultHandler(SapDiApiConnection connection)
    {
        _connection = connection;
    }

    // =====================================================
    // DELIVERY STATUS UPDATE
    // =====================================================
    public void UpdateDeliveryStatus(int docEntry, OdooApiResult result)
    {
        var company = _connection.GetConnectedCompany();
        var doc = (Documents)company.GetBusinessObject(BoObjectTypes.oDeliveryNotes);

        if (!doc.GetByKey(docEntry))
            return;

        if (result.Success)
            OdooUdfMapper.MarkSynced(doc.UserFields, "FROM_SAP");
        else
            OdooUdfMapper.MarkError(doc.UserFields, result.ErrorMessage ?? "Unknown error", "FROM_SAP");

        doc.Update();
    }

    // =====================================================
    // INVOICE STATUS UPDATE
    // =====================================================
    public void UpdateInvoiceStatus(int docEntry, OdooApiResult result)
    {
        var company = _connection.GetConnectedCompany();
        var doc = (Documents)company.GetBusinessObject(BoObjectTypes.oInvoices);

        if (!doc.GetByKey(docEntry))
            return;

        if (result.Success)
            OdooUdfMapper.MarkSynced(doc.UserFields, "FROM_SAP");
        else
            OdooUdfMapper.MarkError(doc.UserFields, result.ErrorMessage ?? "Unknown error", "FROM_SAP");

        doc.Update();
    }

    // =====================================================
    // PAYMENT STATUS UPDATE
    // =====================================================
    public void UpdatePaymentStatus(int docEntry, OdooApiResult result)
    {
        var company = _connection.GetConnectedCompany();
        var pay = (Payments)company.GetBusinessObject(BoObjectTypes.oIncomingPayments);

        if (!pay.GetByKey(docEntry))
            return;

        if (result.Success)
            OdooUdfMapper.MarkSynced(pay.UserFields, "FROM_SAP");
        else
            OdooUdfMapper.MarkError(pay.UserFields, result.ErrorMessage ?? "Unknown error", "FROM_SAP");

        pay.Update();
    }
}