import hashlib
import hmac
import json
import logging
import time

from odoo import http, fields, SUPERUSER_ID
from odoo.http import request

_logger = logging.getLogger(__name__)


class IccWebhookController(http.Controller):
    """Receives JSON payloads from the .NET middleware (OdooApiClient) and
    routes them through the ICC pipeline: authenticate -> idempotency check
    -> process -> log.

    Endpoint pattern:  POST /icc/webhook/<channel_code>

    Expected headers:
        X-API-KEY:      shared secret  (auth_method = api_key)
        X-Signature:    HMAC-SHA256    (auth_method = hmac)
        X-Source-System: e.g. "SAP", "Neon" (optional, for audit)
    """

    # ================================================================
    #  SINGLE UNIFIED WEBHOOK ENDPOINT
    # ================================================================
    @http.route(
        "/icc/webhook/<string:channel_code>",
        type="json",
        auth="none",
        methods=["POST"],
        csrf=False,
    )
    def receive(self, channel_code, **kwargs):
        """Universal webhook receiver — routes by channel code."""
        start = time.time()

        # ── global kill switch ───────────────────────────
        env = request.env(user=SUPERUSER_ID)
        webhooks_enabled = (
            env["ir.config_parameter"].get_param("molas_icc.webhooks_enabled", "True")
        )
        if webhooks_enabled.lower() not in ("true", "1"):
            return {"status": "unavailable", "message": "Webhooks globally disabled"}

        # ── resolve channel ──────────────────────────────
        channel = env["icc.channel"].search(
            [("code", "=", channel_code), ("active", "=", True)], limit=1
        )
        if not channel:
            _logger.warning("ICC webhook: unknown or disabled channel '%s'", channel_code)
            return {"status": "error", "message": f"Channel '{channel_code}' not found or disabled"}

        # ── authenticate ─────────────────────────────────
        auth_error = self._authenticate(channel)
        if auth_error:
            self._log_event(
                env, channel, direction="in", event_type=f"{channel_code}.auth_failed",
                payload=None, state="error", error_message=auth_error,
                processing_ms=int((time.time() - start) * 1000),
            )
            return {"status": "error", "message": auth_error}

        # ── extract payload ──────────────────────────────
        payload = request.jsonrequest
        if not payload:
            return {"status": "error", "message": "Empty payload"}

        source_system = request.httprequest.headers.get("X-Source-System", "unknown")
        source_ref = str(payload.get("SapDocEntry", payload.get("sap_doc_entry", "")))
        event_type = payload.get("event_type", f"{channel_code}.received")

        # ── process ──────────────────────────────────────
        return self._process_event(
            channel=channel,
            event_type=event_type,
            payload=payload,
            source_ref=source_ref,
            source_system=source_system,
            start=start,
            env=env,
        )

    # ================================================================
    #  EVENT PROCESSING
    # ================================================================
    def _process_event(
        self, channel, event_type, payload, source_ref,
        source_system="unknown", parent_event_id=None,
        start=None, env=None,
    ):
        """Core processing logic — can be called from webhook or retry."""
        if start is None:
            start = time.time()
        if env is None:
            env = request.env(user=SUPERUSER_ID)

        code = channel.code

        try:
            # Route to the correct processor
            processor = self._get_processor(code)
            if not processor:
                self._log_event(
                    env, channel, "in", event_type, payload, "error",
                    error_message=f"No processor registered for channel '{code}'",
                    source_ref=source_ref, source_system=source_system,
                    processing_ms=int((time.time() - start) * 1000),
                    parent_event_id=parent_event_id,
                )
                return {"status": "error", "message": f"No processor for '{code}'"}

            result = processor(env, channel, payload)

            elapsed = int((time.time() - start) * 1000)

            if result.get("skipped"):
                self._log_event(
                    env, channel, "in", f"{code}.skipped", payload, "skipped",
                    source_ref=source_ref, source_system=source_system,
                    odoo_model=result.get("odoo_model"),
                    odoo_ref=result.get("odoo_ref"),
                    processing_ms=elapsed,
                    parent_event_id=parent_event_id,
                )
                return {"status": "skipped", "odoo_id": result.get("odoo_ref")}

            self._log_event(
                env, channel, "in", f"{code}.created", payload, "success",
                source_ref=source_ref, source_system=source_system,
                odoo_model=result.get("odoo_model"),
                odoo_ref=result.get("odoo_ref"),
                processing_ms=elapsed,
                parent_event_id=parent_event_id,
            )
            return {"status": "ok", "odoo_id": result.get("odoo_ref")}

        except Exception as exc:
            _logger.exception("ICC processing error on channel '%s'", code)
            elapsed = int((time.time() - start) * 1000)
            self._log_event(
                env, channel, "in", f"{code}.error", payload, "error",
                error_message=str(exc)[:1000],
                source_ref=source_ref, source_system=source_system,
                processing_ms=elapsed,
                parent_event_id=parent_event_id,
            )
            return {"status": "error", "message": str(exc)}

    # ================================================================
    #  PROCESSORS (one per document type)
    # ================================================================
    def _get_processor(self, channel_code):
        """Return the processor function for a given channel code."""
        processors = {
            "invoice": self._process_invoice,
            "delivery": self._process_delivery,
            "payment": self._process_payment,
            "customer": self._process_customer,
            "product": self._process_product,
            "sales_order": self._process_sales_order,
        }
        return processors.get(channel_code)

    # ─── INVOICE ─────────────────────────────────────────
    def _process_invoice(self, env, channel, payload):
        Move = env["account.move"]
        sap_doc_entry = payload.get("SapDocEntry")

        # Idempotency: check by SAP doc entry
        existing = Move.search([("x_sap_doc_entry", "=", sap_doc_entry)], limit=1)
        if existing:
            return {"skipped": True, "odoo_model": "account.move", "odoo_ref": str(existing.id)}

        # Resolve partner
        partner = self._resolve_partner(env, payload.get("CustomerCode"))

        vals = {
            "move_type": "out_invoice",
            "partner_id": partner.id if partner else False,
            "invoice_date": payload.get("InvoiceDate"),
            "x_sap_doc_entry": sap_doc_entry,
            "x_sap_doc_num": payload.get("DocNum"),
            "invoice_line_ids": [],
        }

        # Add lines
        for line in payload.get("Lines", []):
            product = self._resolve_product(env, line.get("ItemCode"))
            vals["invoice_line_ids"].append((0, 0, {
                "product_id": product.id if product else False,
                "name": line.get("Description", ""),
                "quantity": line.get("Quantity", 1),
                "price_unit": line.get("LineTotal", 0) / max(line.get("Quantity", 1), 1),
            }))

        invoice = Move.create(vals)
        return {"odoo_model": "account.move", "odoo_ref": str(invoice.id)}

    # ─── DELIVERY ────────────────────────────────────────
    def _process_delivery(self, env, channel, payload):
        Picking = env["stock.picking"]
        sap_doc_entry = payload.get("SapDocEntry")

        existing = Picking.search([("x_sap_doc_entry", "=", sap_doc_entry)], limit=1)
        if existing:
            # Handle cancellation update
            if payload.get("IsCancelled") and existing.state != "cancel":
                existing.action_cancel()
            return {"skipped": True, "odoo_model": "stock.picking", "odoo_ref": str(existing.id)}

        partner = self._resolve_partner(env, payload.get("CardCode"))

        # Get default picking type for outgoing
        picking_type = env["stock.picking.type"].search(
            [("code", "=", "outgoing")], limit=1
        )

        vals = {
            "partner_id": partner.id if partner else False,
            "picking_type_id": picking_type.id if picking_type else False,
            "scheduled_date": payload.get("DeliveryDate"),
            "x_sap_doc_entry": sap_doc_entry,
            "x_sap_doc_num": payload.get("SapDocNum"),
            "move_ids_without_package": [],
        }

        for line in payload.get("Lines", []):
            product = self._resolve_product(env, line.get("ItemCode"))
            if product:
                vals["move_ids_without_package"].append((0, 0, {
                    "product_id": product.id,
                    "name": line.get("Description", product.name),
                    "product_uom_qty": line.get("Quantity", 0),
                    "product_uom": product.uom_id.id,
                    "location_id": picking_type.default_location_src_id.id if picking_type else False,
                    "location_dest_id": picking_type.default_location_dest_id.id if picking_type else False,
                }))

        picking = Picking.create(vals)
        return {"odoo_model": "stock.picking", "odoo_ref": str(picking.id)}

    # ─── PAYMENT ─────────────────────────────────────────
    def _process_payment(self, env, channel, payload):
        Payment = env["account.payment"]
        sap_doc_entry = payload.get("SapDocEntry")

        existing = Payment.search([("x_sap_doc_entry", "=", sap_doc_entry)], limit=1)
        if existing:
            return {"skipped": True, "odoo_model": "account.payment", "odoo_ref": str(existing.id)}

        partner = self._resolve_partner(env, payload.get("CustomerCode"))

        vals = {
            "payment_type": "inbound",
            "partner_type": "customer",
            "partner_id": partner.id if partner else False,
            "amount": payload.get("DocTotal", 0),
            "date": payload.get("DocDate"),
            "x_sap_doc_entry": sap_doc_entry,
        }

        payment = Payment.create(vals)
        return {"odoo_model": "account.payment", "odoo_ref": str(payment.id)}

    # ─── CUSTOMER ────────────────────────────────────────
    def _process_customer(self, env, channel, payload):
        Partner = env["res.partner"]
        card_code = payload.get("CardCode")

        existing = Partner.search([("x_sap_card_code", "=", card_code)], limit=1)
        if existing:
            # Update existing partner
            update_vals = self._build_partner_vals(payload)
            existing.write(update_vals)
            return {"odoo_model": "res.partner", "odoo_ref": str(existing.id)}

        vals = self._build_partner_vals(payload)
        vals["x_sap_card_code"] = card_code
        partner = Partner.create(vals)
        return {"odoo_model": "res.partner", "odoo_ref": str(partner.id)}

    # ─── PRODUCT ─────────────────────────────────────────
    def _process_product(self, env, channel, payload):
        Product = env["product.product"]
        item_code = payload.get("ItemCode")

        existing = Product.search([("default_code", "=", item_code)], limit=1)
        if existing:
            update_vals = {}
            if payload.get("ItemName"):
                update_vals["name"] = payload["ItemName"]
            if payload.get("Price"):
                update_vals["list_price"] = payload["Price"]
            if update_vals:
                existing.write(update_vals)
            return {"odoo_model": "product.product", "odoo_ref": str(existing.id)}

        vals = {
            "default_code": item_code,
            "name": payload.get("ItemName", item_code),
            "list_price": payload.get("Price", 0),
            "type": "product",
        }
        product = Product.create(vals)
        return {"odoo_model": "product.product", "odoo_ref": str(product.id)}

    # ─── SALES ORDER ─────────────────────────────────────
    def _process_sales_order(self, env, channel, payload):
        Order = env["sale.order"]
        sap_doc_entry = payload.get("SapDocEntry")

        existing = Order.search([("x_sap_doc_entry", "=", sap_doc_entry)], limit=1)
        if existing:
            return {"skipped": True, "odoo_model": "sale.order", "odoo_ref": str(existing.id)}

        partner = self._resolve_partner(env, payload.get("CardCode"))

        vals = {
            "partner_id": partner.id if partner else False,
            "x_sap_doc_entry": sap_doc_entry,
            "order_line": [],
        }

        for line in payload.get("Lines", []):
            product = self._resolve_product(env, line.get("ItemCode"))
            vals["order_line"].append((0, 0, {
                "product_id": product.id if product else False,
                "name": line.get("Description", ""),
                "product_uom_qty": line.get("Quantity", 1),
                "price_unit": line.get("UnitPrice", 0),
            }))

        order = Order.create(vals)
        return {"odoo_model": "sale.order", "odoo_ref": str(order.id)}

    # ================================================================
    #  HELPERS
    # ================================================================
    def _authenticate(self, channel):
        """Validate request credentials against channel config.
        Returns error message string on failure, None on success.
        """
        if channel.auth_method == "api_key":
            api_key = request.httprequest.headers.get("X-API-KEY")
            if not api_key or api_key != channel.api_key:
                return "Invalid or missing API key"

        elif channel.auth_method == "hmac":
            signature = request.httprequest.headers.get("X-Signature")
            if not signature or not channel.hmac_secret:
                return "Missing HMAC signature or secret not configured"

            body = request.httprequest.get_data()
            expected = hmac.new(
                channel.hmac_secret.encode(),
                body,
                hashlib.sha256,
            ).hexdigest()

            if not hmac.compare_digest(signature, expected):
                return "Invalid HMAC signature"

        return None

    def _resolve_partner(self, env, card_code):
        """Find partner by SAP CardCode."""
        if not card_code:
            return None
        return env["res.partner"].search(
            [("x_sap_card_code", "=", card_code)], limit=1
        ) or None

    def _resolve_product(self, env, item_code):
        """Find product by SAP ItemCode (default_code)."""
        if not item_code:
            return None
        return env["product.product"].search(
            [("default_code", "=", item_code)], limit=1
        ) or None

    def _build_partner_vals(self, payload):
        """Build partner vals dict from payload."""
        vals = {}
        if payload.get("CardName"):
            vals["name"] = payload["CardName"]
        if payload.get("Email"):
            vals["email"] = payload["Email"]
        if payload.get("Phone"):
            vals["phone"] = payload["Phone"]
        if payload.get("Address"):
            vals["street"] = payload["Address"]
        if payload.get("City"):
            vals["city"] = payload["City"]
        if payload.get("Country"):
            country = request.env["res.country"].sudo().search(
                [("code", "=", payload["Country"])], limit=1
            )
            if country:
                vals["country_id"] = country.id
        vals["customer_rank"] = 1
        return vals

    def _log_event(
        self, env, channel, direction, event_type, payload,
        state, error_message=None, source_ref=None,
        source_system=None, odoo_model=None, odoo_ref=None,
        processing_ms=0, parent_event_id=None,
    ):
        """Write an entry to icc.event.log."""
        try:
            env["icc.event.log"].create({
                "channel_id": channel.id,
                "direction": direction,
                "event_type": event_type,
                "source_system": source_system,
                "source_ref": source_ref,
                "odoo_model": odoo_model,
                "odoo_ref": odoo_ref,
                "payload": json.dumps(payload) if payload else None,
                "state": state,
                "error_message": error_message,
                "processing_ms": processing_ms,
                "parent_event_id": parent_event_id,
            })
        except Exception:
            _logger.exception("Failed to write ICC event log")

    # ================================================================
    #  HEALTH CHECK ENDPOINT
    # ================================================================
    @http.route("/icc/health", type="json", auth="none", methods=["GET"], csrf=False)
    def health(self, **kwargs):
        """Simple health check for monitoring."""
        return {"status": "ok", "service": "molas_icc"}
