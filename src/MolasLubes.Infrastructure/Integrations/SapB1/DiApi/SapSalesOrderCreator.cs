using SAPbobsCOM;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.Orders;
using MolasLubes.Infrastructure.Integrations.SapB1.Helpers;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapSalesOrderCreator
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapSalesOrderCreator> _logger;

    public SapSalesOrderCreator(
        SapDiApiConnection connection,
        ILogger<SapSalesOrderCreator> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // =====================================================
    // CREATE SALES ORDER (ODOO → SAP)
    // =====================================================
    public (int DocEntry, int DocNum) CreateOrder(CreateSalesOrderDto dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));

        _logger.LogInformation(
            "Incoming CreateOrder | Customer={Customer} | ExternalOrderId={ExternalId}",
            dto.CustomerCode,
            dto.ExternalOrderId);

        if (string.IsNullOrWhiteSpace(dto.CustomerCode))
            throw new ArgumentException("CustomerCode is required");

        if (dto.Lines == null || dto.Lines.Count == 0)
            throw new ArgumentException("At least one order line is required");

        var company = _connection.GetConnectedCompany();
        var order = (Documents)company.GetBusinessObject(BoObjectTypes.oOrders);

        try
        {
            var today = DateTime.Today;

            // =====================
            // HEADER
            // =====================
            order.CardCode = dto.CustomerCode.Trim();
            order.DocDate = today;
            order.TaxDate = today;
            order.DocDueDate = dto.DeliveryDate ?? today;

            order.DocCurrency = string.IsNullOrWhiteSpace(dto.Currency)
                ? "TZS"
                : dto.Currency.Trim();

            // =====================
            // 🔗 ODOO SALES ORDER ID → SAP UDF
            // =====================
            if (!string.IsNullOrWhiteSpace(dto.ExternalOrderId))
            {
                string externalId = dto.ExternalOrderId.Trim();

                _logger.LogInformation(
                    "Assigning U_Odoo_SO_ID | Value={Value}",
                    externalId);

                order.UserFields.Fields
                    .Item("U_Odoo_SO_ID").Value = externalId;
            }
            else
            {
                _logger.LogWarning(
                    "ExternalOrderId is NULL or empty — U_Odoo_SO_ID will NOT be assigned.");
            }

            // =====================
            // CENTRALIZED UDF MAPPER
            // =====================
            OdooUdfMapper.ApplySalesOrderUdfs(
                order,
                dto.ExternalOrderId,
                "O2S"
            );

            // =====================
            // LINES
            // =====================
            foreach (var line in dto.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.ItemCode))
                    throw new ArgumentException("Line ItemCode is required");

                if (line.Quantity <= 0)
                    throw new ArgumentException("Line Quantity must be > 0");

                order.Lines.ItemCode = line.ItemCode.Trim();
                order.Lines.Quantity = (double)line.Quantity;

                // ── PRE-ADD DESCRIPTION FORMATTING ────────────────────────
                // After setting ItemCode, SAP auto-fills ItemDescription from
                // OITM.ItemName.  We read that value, then apply the
                // "ItemName/Manufacturer/OriginalDescription" prefix in the
                // same Add() transaction — no second Update() required.
                // If line.ItemName and line.Manufacturer are both blank (e.g.
                // when creating from reservations), the description is left as
                // SAP populated it.
                {
                    string currentDesc = (order.Lines.ItemDescription ?? string.Empty).Trim();
                    order.Lines.ItemDescription = SapSalesOrderLineDescriptionUpdater.BuildDescription(
                        line.ItemName,
                        line.Manufacturer,
                        currentDesc);
                }
                // ─────────────────────────────────────────────────────────

                if (line.Price.HasValue && line.Price.Value > 0)
                    order.Lines.Price = (double)line.Price.Value;

                if (!string.IsNullOrWhiteSpace(line.ExternalLineId))
                {
                    order.Lines.UserFields.Fields
                        .Item("U_OdooSOLineId").Value =
                            line.ExternalLineId.Trim();
                }

                order.Lines.Add();
            }

            // =====================
            // COMMIT
            // =====================
            int rc = order.Add();

            if (rc != 0)
            {
                company.GetLastError(out int code, out string msg);
                throw new Exception($"SAP Order failed ({code}): {msg}");
            }

            int docEntry = int.Parse(company.GetNewObjectKey());

            order.GetByKey(docEntry);
            int docNum = order.DocNum;

            // 🔍 VERIFY UDF AFTER CREATION
            string? savedExternalId = null;

            try
            {
                var udfValue = order.UserFields.Fields
                    .Item("U_Odoo_SO_ID").Value;

                savedExternalId = udfValue?.ToString();
            }
            catch
            {
                _logger.LogWarning(
                    "U_Odoo_SO_ID not found on created document.");
            }

            _logger.LogInformation(
                "SAP Sales Order created | DocEntry={DocEntry} | DocNum={DocNum} | SentOdooSO={SentId} | SavedUdf={SavedId}",
                docEntry,
                docNum,
                dto.ExternalOrderId,
                savedExternalId);

            return (docEntry, docNum);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SAP Sales Order creation failed | OdooSO={OdooSO}",
                dto.ExternalOrderId);

            throw;
        }
    }
}