{
    'name': 'Molas SAP B1 Integration',
    'version': '19.0.1.0.0',
    'category': 'Integration',
    'summary': 'REST API endpoints for SAP B1 → Odoo data push (deliveries, invoices, payments)',
    'description': """
        Exposes three REST endpoints that accept JSON POSTs from the SAP B1 .NET bridge:
          POST /api/deliveries  — creates/updates stock.picking
          POST /api/invoices    — creates/updates account.move (out_invoice)
          POST /api/payments    — creates/updates account.payment

        Authentication: X-API-KEY header validated against the
        'molas_sap_integration.api_key' system parameter.
    """,
    'depends': ['account', 'stock'],
    'data': [
        'data/ir_config_parameter_data.xml',
    ],
    'installable': True,
    'auto_install': False,
    'license': 'LGPL-3',
}
