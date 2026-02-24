from odoo import fields, models


class AccountPayment(models.Model):
    _inherit = 'account.payment'

    x_sap_doc_entry = fields.Integer(
        string='SAP Doc Entry',
        index=True,
        default=0,
        help='SAP ORCT DocEntry — used as the upsert key by the integration API.',
    )
    x_sap_doc_num = fields.Integer(
        string='SAP Doc Num',
        default=0,
    )
    x_sap_invoice_entry = fields.Integer(
        string='SAP Invoice Doc Entry',
        default=0,
        help='SAP OINV DocEntry this payment was applied to.',
    )
