from odoo import fields, models


class StockPicking(models.Model):
    _inherit = 'stock.picking'

    x_sap_doc_entry = fields.Integer(
        string='SAP Doc Entry',
        index=True,
        default=0,
        help='SAP ODLN DocEntry — used as the upsert key by the integration API.',
    )
    x_sap_doc_num = fields.Integer(
        string='SAP Doc Num',
        default=0,
    )
