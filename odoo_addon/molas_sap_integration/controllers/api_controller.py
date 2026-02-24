import json
import logging

from odoo import http
from odoo.http import request, Response

_logger = logging.getLogger(__name__)


class SapIntegrationController(http.Controller):

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    def _check_api_key(self):
        """Validate X-API-KEY header against ir.config_parameter."""
        key = request.httprequest.headers.get('X-API-KEY', '')
        expected = (
            request.env['ir.config_parameter']
            .sudo()
            .get_param('molas_sap_integration.api_key', '')
        )
        return bool(expected) and key == expected

    def _ok(self, data=None):
        return Response(
            json.dumps(data or {'status': 'ok'}),
            status=200,
            mimetype='application/json',
        )

    def _error(self, message, status=400):
        _logger.warning('SAP API error %s: %s', status, message)
        return Response(
            json.dumps({'error': message}),
            status=status,
            mimetype='application/json',
        )

    def _parse_body(self):
        try:
            return json.loads(request.httprequest.data or '{}'), None
        except json.JSONDecodeError as exc:
            return None, str(exc)

    def _find_partner(self, card_code):
        return (
            request.env['res.partner']
            .sudo()
            .search([('ref', '=', card_code), ('active', 'in', [True, False])], limit=1)
        )

    def _default_uom(self):
        return request.env.ref('uom.product_uom_unit', raise_if_not_found=False)

    def _default_income_account(self, company):
        return (
            request.env['account.account']
            .sudo()
            .search([
                ('account_type', '=', 'income'),
                ('company_ids', 'in', company.id),
                ('deprecated', '=', False),
            ], limit=1)
        )

    # ------------------------------------------------------------------
    # POST /api/deliveries
    # ------------------------------------------------------------------

    @http.route(
        '/api/deliveries',
        type='http',
        auth='none',
        methods=['POST'],
        csrf=False,
        save_session=False,
    )
    def receive_delivery(self, **_kwargs):
        if not self._check_api_key():
            return self._error('Unauthorized', 401)

        data, err = self._parse_body()
        if err:
            return self._error(f'Invalid JSON: {err}', 400)

        try:
            result = self._process_delivery(data)
            return self._ok(result)
        except Exception as exc:
            _logger.exception(
                'Error processing delivery sap_doc_entry=%s', data.get('sap_doc_entry')
            )
            return self._error(str(exc), 500)

    def _process_delivery(self, data):
        env = request.env
        sap_doc_entry = int(data['sap_doc_entry'])

        partner = self._find_partner(data['card_code'])
        if not partner:
            raise ValueError(f"Partner not found for card_code={data['card_code']!r}")

        picking = (
            env['stock.picking']
            .sudo()
            .search([('x_sap_doc_entry', '=', sap_doc_entry)], limit=1)
        )

        if not picking:
            picking_type = (
                env['stock.picking.type']
                .sudo()
                .search(
                    [('code', '=', 'outgoing'), ('active', 'in', [True, False])],
                    order='sequence asc',
                    limit=1,
                )
            )
            picking = env['stock.picking'].sudo().create({
                'x_sap_doc_entry': sap_doc_entry,
                'x_sap_doc_num': int(data.get('sap_doc_num', 0) or 0),
                'partner_id': partner.id,
                'picking_type_id': picking_type.id,
                'scheduled_date': data.get('delivery_date'),
                'origin': f"SAP ODLN-{data.get('sap_doc_num')}",
            })
        else:
            if picking.state == 'draft':
                picking.sudo().write({
                    'partner_id': partner.id,
                    'scheduled_date': data.get('delivery_date'),
                })
                picking.sudo().move_ids.unlink()

        if picking.state == 'draft':
            uom_unit = self._default_uom()
            for line in data.get('lines', []):
                product = (
                    env['product.product']
                    .sudo()
                    .search([('default_code', '=', line['item_code'])], limit=1)
                )
                env['stock.move'].sudo().create({
                    'picking_id': picking.id,
                    'name': line.get('description') or line['item_code'],
                    'product_id': product.id if product else False,
                    'product_uom_qty': float(line.get('quantity', 0)),
                    'product_uom': (
                        product.uom_id.id if product
                        else (uom_unit.id if uom_unit else False)
                    ),
                    'location_id': picking.location_id.id,
                    'location_dest_id': picking.location_dest_id.id,
                })

        if data.get('is_cancelled') and picking.state not in ('done', 'cancel'):
            picking.sudo().action_cancel()

        return {'status': 'ok', 'picking_id': picking.id}

    # ------------------------------------------------------------------
    # POST /api/invoices
    # ------------------------------------------------------------------

    @http.route(
        '/api/invoices',
        type='http',
        auth='none',
        methods=['POST'],
        csrf=False,
        save_session=False,
    )
    def receive_invoice(self, **_kwargs):
        if not self._check_api_key():
            return self._error('Unauthorized', 401)

        data, err = self._parse_body()
        if err:
            return self._error(f'Invalid JSON: {err}', 400)

        try:
            result = self._process_invoice(data)
            return self._ok(result)
        except Exception as exc:
            _logger.exception(
                'Error processing invoice sap_doc_entry=%s', data.get('sap_doc_entry')
            )
            return self._error(str(exc), 500)

    def _process_invoice(self, data):
        env = request.env
        sap_doc_entry = int(data['sap_doc_entry'])

        partner = self._find_partner(data['customer_code'])
        if not partner:
            raise ValueError(f"Partner not found for customer_code={data['customer_code']!r}")

        move = (
            env['account.move']
            .sudo()
            .search(
                [
                    ('x_sap_doc_entry', '=', sap_doc_entry),
                    ('move_type', '=', 'out_invoice'),
                ],
                limit=1,
            )
        )

        if not move:
            move = env['account.move'].sudo().create({
                'x_sap_doc_entry': sap_doc_entry,
                'x_sap_doc_num': int(data.get('doc_num', 0) or 0),
                'move_type': 'out_invoice',
                'partner_id': partner.id,
                'invoice_date': data.get('invoice_date'),
                'ref': f"SAP OINV-{data.get('doc_num')}",
            })
        elif move.state == 'draft':
            move.sudo().write({
                'partner_id': partner.id,
                'invoice_date': data.get('invoice_date'),
            })
            move.sudo().invoice_line_ids.unlink()

        if move.state == 'draft':
            default_account = self._default_income_account(move.company_id)
            line_commands = []
            for line in data.get('lines', []):
                product = (
                    env['product.product']
                    .sudo()
                    .search([('default_code', '=', line['item_code'])], limit=1)
                )
                qty = float(line.get('quantity') or 1)
                price_unit = float(line.get('line_total', 0)) / qty

                line_vals = {
                    'name': line.get('description') or line['item_code'],
                    'quantity': qty,
                    'price_unit': price_unit,
                }
                if product:
                    line_vals['product_id'] = product.id
                elif default_account:
                    line_vals['account_id'] = default_account.id

                line_commands.append((0, 0, line_vals))

            if line_commands:
                move.sudo().write({'invoice_line_ids': line_commands})

        return {'status': 'ok', 'move_id': move.id}

    # ------------------------------------------------------------------
    # POST /api/payments
    # ------------------------------------------------------------------

    @http.route(
        '/api/payments',
        type='http',
        auth='none',
        methods=['POST'],
        csrf=False,
        save_session=False,
    )
    def receive_payment(self, **_kwargs):
        if not self._check_api_key():
            return self._error('Unauthorized', 401)

        data, err = self._parse_body()
        if err:
            return self._error(f'Invalid JSON: {err}', 400)

        try:
            result = self._process_payment(data)
            return self._ok(result)
        except Exception as exc:
            _logger.exception(
                'Error processing payment sap_doc_entry=%s', data.get('sap_doc_entry')
            )
            return self._error(str(exc), 500)

    def _process_payment(self, data):
        env = request.env
        sap_doc_entry = int(data['sap_doc_entry'])
        invoice_entry = int(data.get('invoice_entry', 0) or 0)

        partner = self._find_partner(data['customer_code'])
        if not partner:
            raise ValueError(f"Partner not found for customer_code={data['customer_code']!r}")

        # Idempotent: one SAP payment per invoice link
        existing = (
            env['account.payment']
            .sudo()
            .search([
                ('x_sap_doc_entry', '=', sap_doc_entry),
                ('x_sap_invoice_entry', '=', invoice_entry),
            ], limit=1)
        )
        if existing:
            return {'status': 'ok', 'payment_id': existing.id, 'created': False}

        journal = (
            env['account.journal']
            .sudo()
            .search([
                ('type', 'in', ('bank', 'cash')),
                ('company_id', '=', env.company.id),
            ], order='type desc', limit=1)
        )

        payment = env['account.payment'].sudo().create({
            'x_sap_doc_entry': sap_doc_entry,
            'x_sap_doc_num': int(data.get('doc_num', 0) or 0),
            'x_sap_invoice_entry': invoice_entry,
            'payment_type': 'inbound',
            'partner_type': 'customer',
            'partner_id': partner.id,
            'amount': float(data.get('amount', 0)),
            'date': data.get('payment_date'),
            'journal_id': journal.id if journal else False,
            'ref': f"SAP ORCT-{data.get('doc_num')} / INV-{invoice_entry}",
        })

        return {'status': 'ok', 'payment_id': payment.id, 'created': True}
