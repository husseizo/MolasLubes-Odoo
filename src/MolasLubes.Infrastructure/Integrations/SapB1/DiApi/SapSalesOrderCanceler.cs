using SAPbobsCOM;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapSalesOrderCanceler
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapSalesOrderCanceler> _logger;

    public SapSalesOrderCanceler(
        SapDiApiConnection connection,
        ILogger<SapSalesOrderCanceler> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public void Cancel(int docEntry, string reason)
    {
        var company = _connection.GetConnectedCompany();
        var order = (Documents)company.GetBusinessObject(BoObjectTypes.oOrders);

        if (!order.GetByKey(docEntry))
            throw new InvalidOperationException("SAP Sales Order not found");

        if (order.DocumentStatus == BoStatus.bost_Close)
            throw new InvalidOperationException("Sales Order already closed");

        try
        {
            // =====================
            // BUSINESS COMMENT
            // =====================
            order.Comments = $"Cancelled: {reason}";

            // =====================
            // 🔗 ODOO UDFS
            // =====================
            order.UserFields.Fields.Item(OdooUdfs.Status).Value = "SYNCED";
            order.UserFields.Fields.Item(OdooUdfs.SyncDir).Value = "FROM_SAP";
            order.UserFields.Fields.Item(OdooUdfs.LastSync).Value = DateTime.Today;

            // =====================
            // CANCEL
            // =====================
            var rc = order.Cancel();
            if (rc != 0)
            {
                company.GetLastError(out var code, out var msg);
                throw new Exception($"SAP cancel failed ({code}): {msg}");
            }

            _logger.LogInformation(
                "🛑 SAP Sales Order cancelled | DocEntry={DocEntry}",
                docEntry);
        }
        catch (Exception ex)
        {
            // =====================
            // 🔥 ERROR TRACKING
            // =====================
            try
            {
                order.UserFields.Fields.Item(OdooUdfs.Status).Value = "ERROR";
                order.UserFields.Fields.Item(OdooUdfs.ErrorMsg).Value =
                    ex.Message.Length > 250
                        ? ex.Message[..250]
                        : ex.Message;

                order.UserFields.Fields.Item(OdooUdfs.LastSync).Value = DateTime.Today;
            }
            catch
            {
                // ignore secondary UDF failures
            }

            _logger.LogError(
                ex,
                "❌ SAP Sales Order cancel failed | DocEntry={DocEntry}",
                docEntry);

            throw;
        }
    }
}