namespace MolasLubes.Infrastructure.Integrations.SapB1.Udfs;

public static class OdooUdfs
{
    // =========================
    // COMMON (ALL DOCUMENTS)
    // =========================
    public const string Status = "U_Odoo_Status";     // PENDING | SYNCED | ERROR
    public const string ErrorMsg = "U_Odoo_ErrorMsg";   // Error message (255)
    public const string LastSync = "U_Odoo_LastSync";   // Date
    public const string SyncDir = "U_Odoo_SyncDir";    // TO_SAP | FROM_SAP | BIDIR

    // =========================
    // SALES
    // =========================
    public const string SalesOrderId = "U_Odoo_SO_ID";       // ORDR
    public const string SalesOrderLineId = "U_Odoo_SOLine_ID";   // RDR1

    // =========================
    // DELIVERY / RETURNS
    // =========================
    public const string DeliveryId = "U_Odoo_Delivery_ID";     // ODLN
    public const string DeliveryMoveId = "U_Odoo_Move_ID";         // DLN1
    public const string ReturnId = "U_Odoo_Return_ID";       // ORDN
    public const string ReturnMoveId = "U_Odoo_ReturnMove_ID";   // RDN1

    // =========================
    // INVOICES / CREDIT
    // =========================
    public const string InvoiceId = "U_Odoo_Invoice_ID";   // OINV
    public const string InvoiceLineId = "U_Odoo_InvLine_ID";   // INV1
    public const string CreditNoteId = "U_Odoo_Credit_ID";    // ORIN

    // CREDIT MEMO (ORIN)
    public const string CreditId = "U_Odoo_Credit_ID";

    // =========================
    // PAYMENTS
    // =========================
    public const string PaymentId = "U_Odoo_Payment_ID";   // ORCT
    public const string PaymentLineId = "U_Odoo_PayLine_ID";   // RCT2

    // =========================
    // BUSINESS PARTNER
    // =========================
    public const string PartnerId = "U_Odoo_Partner_ID";  // OCRD

    // =========================
    // PRODUCTS / PRICES
    // =========================
    public const string ProductId = "U_Odoo_Product_ID";     // OITM
    public const string PricelistId = "U_Odoo_Pricelist_ID";   // OPLN
    public const string PricelistItemId = "U_Odoo_PriceItem_ID";   // ITM1
}