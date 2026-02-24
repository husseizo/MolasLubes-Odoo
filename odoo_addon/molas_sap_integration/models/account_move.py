from odoo import fields, models


class AccountMove(models.Model):
    _inherit = 'account.move'

    x_sap_doc_entry = fields.Integer(
        string='SAP Doc Entry',
        index=True,
        default=0,
        help='SAP OINV DocEntry — used as the upsert key by the integration API.',
    )
    x_sap_doc_num = fields.Integer(
        string='SAP Doc Num',
        default=0,
    )
