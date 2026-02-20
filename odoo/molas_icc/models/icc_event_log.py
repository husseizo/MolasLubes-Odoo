import json
import logging

from odoo import api, fields, models

_logger = logging.getLogger(__name__)


class IccEventLog(models.Model):
    """Audit trail for every inbound webhook hit and outbound push."""

    _name = "icc.event.log"
    _description = "Integration Event Log"
    _order = "create_date desc"
    _rec_name = "display_name"

    # ── references ───────────────────────────────────────
    channel_id = fields.Many2one(
        "icc.channel", required=True, ondelete="cascade", index=True
    )
    channel_code = fields.Char(related="channel_id.code", store=True)

    # ── direction & type ─────────────────────────────────
    direction = fields.Selection(
        [("in", "Inbound"), ("out", "Outbound")],
        required=True,
        index=True,
    )
    event_type = fields.Char(
        index=True,
        help="Dot-separated event type, e.g. 'invoice.created', 'delivery.updated'.",
    )

    # ── cross-references ─────────────────────────────────
    source_system = fields.Char(
        help="Originating system identifier, e.g. 'SAP', 'Neon'."
    )
    source_ref = fields.Char(
        index=True,
        help="External reference (SAP DocEntry, Neon primary key).",
    )
    odoo_model = fields.Char(help="Target Odoo model, e.g. 'account.move'.")
    odoo_ref = fields.Char(help="Odoo record ID that was created or updated.")

    # ── payload ──────────────────────────────────────────
    payload = fields.Text(help="Raw JSON payload received or sent.")
    payload_preview = fields.Char(
        compute="_compute_payload_preview", store=False
    )

    # ── result ───────────────────────────────────────────
    state = fields.Selection(
        [
            ("success", "Success"),
            ("error", "Error"),
            ("skipped", "Skipped (Duplicate)"),
        ],
        required=True,
        index=True,
        default="success",
    )
    error_message = fields.Text()
    http_status = fields.Integer(help="HTTP status code returned to caller.")
    processing_ms = fields.Integer(
        string="Processing (ms)",
        help="Wall-clock time to process this event.",
    )

    # ── retry tracking ───────────────────────────────────
    retry_count = fields.Integer(default=0)
    retried_at = fields.Datetime()
    parent_event_id = fields.Many2one(
        "icc.event.log",
        string="Original Event",
        help="If this is a retry, link to the original failed event.",
    )

    # ── computed fields ──────────────────────────────────
    display_name = fields.Char(compute="_compute_display_name", store=False)

    @api.depends("channel_code", "event_type", "source_ref")
    def _compute_display_name(self):
        for rec in self:
            parts = [rec.channel_code or "", rec.event_type or "", rec.source_ref or ""]
            rec.display_name = " / ".join(filter(None, parts)) or "Event"

    def _compute_payload_preview(self):
        for rec in self:
            if rec.payload:
                preview = rec.payload[:120]
                rec.payload_preview = preview + ("..." if len(rec.payload) > 120 else "")
            else:
                rec.payload_preview = ""

    # ── actions ──────────────────────────────────────────
    def action_retry(self):
        """Re-dispatch this event through the webhook processor."""
        self.ensure_one()
        if self.state != "error":
            return
        if not self.payload:
            return

        channel = self.channel_id
        if not channel or not channel.active:
            return

        # Import here to avoid circular dependency
        from ..controllers.webhook import IccWebhookController

        controller = IccWebhookController()
        payload = json.loads(self.payload)
        result = controller._process_event(
            channel=channel,
            event_type=self.event_type,
            payload=payload,
            source_ref=self.source_ref,
            source_system=self.source_system or "retry",
            parent_event_id=self.id,
        )
        self.retry_count += 1
        self.retried_at = fields.Datetime.now()
        return result

    def action_view_payload(self):
        """Open a popup showing the full JSON payload."""
        self.ensure_one()
        return {
            "type": "ir.actions.act_window",
            "name": "Event Payload",
            "res_model": "icc.event.log",
            "res_id": self.id,
            "view_mode": "form",
            "target": "new",
        }

    # ── cron ────────────────────────────────────────────
    @api.model
    def _cron_purge_old_logs(self):
        """Delete event logs older than the configured retention period."""
        retention_days = int(
            self.env["ir.config_parameter"]
            .sudo()
            .get_param("molas_icc.log_retention_days", "90")
        )
        cutoff = fields.Datetime.subtract(
            fields.Datetime.now(), days=retention_days
        )
        old_logs = self.sudo().search([("create_date", "<", cutoff)])
        count = len(old_logs)
        if count:
            old_logs.unlink()
            _logger.info("ICC log cleanup: purged %d events older than %s", count, cutoff)
        else:
            _logger.info("ICC log cleanup: nothing to purge")
