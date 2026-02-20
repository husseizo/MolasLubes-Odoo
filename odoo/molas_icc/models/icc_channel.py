import logging
import secrets

from odoo import api, fields, models

_logger = logging.getLogger(__name__)


class IccChannel(models.Model):
    """Webhook endpoint registry.

    Each channel represents one integration endpoint (e.g. invoice, delivery,
    payment).  Operators can toggle channels on/off and managers can rotate
    API keys — all from the ICC dashboard inside Odoo.
    """

    _name = "icc.channel"
    _description = "Integration Channel"
    _order = "sequence, name"

    # ── identity ─────────────────────────────────────────
    name = fields.Char(required=True)
    code = fields.Char(
        required=True,
        copy=False,
        help="Machine-readable code used in the webhook URL, e.g. 'invoice'.",
    )
    sequence = fields.Integer(default=10)
    active = fields.Boolean(default=True)
    description = fields.Text()

    # ── endpoint ─────────────────────────────────────────
    endpoint = fields.Char(
        compute="_compute_endpoint",
        store=False,
        help="Full webhook URL that the .NET middleware should POST to.",
    )

    # ── authentication ───────────────────────────────────
    auth_method = fields.Selection(
        [
            ("api_key", "API Key"),
            ("hmac", "HMAC-SHA256 Signature"),
        ],
        default="api_key",
        required=True,
    )
    api_key = fields.Char(
        groups="molas_icc.group_icc_manager",
        help="Shared secret sent via X-API-KEY header.",
    )
    hmac_secret = fields.Char(
        groups="molas_icc.group_icc_manager",
        help="Secret for HMAC-SHA256 body signature (X-Signature header).",
    )

    # ── direction ────────────────────────────────────────
    direction = fields.Selection(
        [
            ("inbound", "External -> Odoo"),
            ("outbound", "Odoo -> External"),
            ("bidirectional", "Both"),
        ],
        default="inbound",
        required=True,
    )

    # ── target Odoo model ────────────────────────────────
    target_model = fields.Char(
        help="Odoo model that this channel creates/updates, e.g. 'account.move'.",
    )

    # ── computed stats ───────────────────────────────────
    last_event_date = fields.Datetime(
        compute="_compute_stats", store=False
    )
    event_count_24h = fields.Integer(
        string="Events (24h)", compute="_compute_stats", store=False
    )
    error_count_24h = fields.Integer(
        string="Errors (24h)", compute="_compute_stats", store=False
    )
    health_state = fields.Selection(
        [
            ("healthy", "Healthy"),
            ("warning", "Warning"),
            ("error", "Error"),
            ("idle", "Idle"),
        ],
        compute="_compute_stats",
        store=False,
    )

    # ── SQL constraint ───────────────────────────────────
    _sql_constraints = [
        ("code_unique", "UNIQUE(code)", "Channel code must be unique."),
    ]

    # ── computes ─────────────────────────────────────────
    @api.depends("code")
    def _compute_endpoint(self):
        base = self.env["ir.config_parameter"].sudo().get_param("web.base.url", "")
        for rec in self:
            rec.endpoint = f"{base}/icc/webhook/{rec.code}" if rec.code else ""

    def _compute_stats(self):
        cutoff = fields.Datetime.subtract(fields.Datetime.now(), hours=24)
        EventLog = self.env["icc.event.log"].sudo()
        for rec in self:
            domain = [("channel_id", "=", rec.id)]
            domain_24h = domain + [("create_date", ">=", cutoff)]

            last = EventLog.search(domain, limit=1, order="create_date desc")
            rec.last_event_date = last.create_date if last else False

            rec.event_count_24h = EventLog.search_count(domain_24h)
            rec.error_count_24h = EventLog.search_count(
                domain_24h + [("state", "=", "error")]
            )

            if not last:
                rec.health_state = "idle"
            elif rec.error_count_24h > 10:
                rec.health_state = "error"
            elif rec.error_count_24h > 0:
                rec.health_state = "warning"
            else:
                rec.health_state = "healthy"

    # ── actions ──────────────────────────────────────────
    def action_generate_api_key(self):
        """Generate a new 48-char URL-safe API key."""
        for rec in self:
            rec.api_key = secrets.token_urlsafe(36)

    def action_view_events(self):
        """Open event log filtered to this channel."""
        self.ensure_one()
        return {
            "type": "ir.actions.act_window",
            "name": f"Events: {self.name}",
            "res_model": "icc.event.log",
            "view_mode": "tree,form",
            "domain": [("channel_id", "=", self.id)],
            "context": {"default_channel_id": self.id},
        }

    # ── cron ────────────────────────────────────────────
    @api.model
    def _cron_fallback_poll(self):
        """Safety-net cron: poll Neon for records that webhooks may have missed.

        This method is intentionally a stub. The actual polling logic depends
        on your Neon DB schema and connection settings (configured in ICC
        Settings > Neon DSN).  Implement the query against Neon's tables
        for records where OdooStatus IS NULL and push them through the
        same processor pipeline.
        """
        enabled = self.env["ir.config_parameter"].sudo().get_param(
            "molas_icc.fallback_cron_enabled", "True"
        )
        if enabled.lower() not in ("true", "1"):
            return

        neon_dsn = self.env["ir.config_parameter"].sudo().get_param(
            "molas_icc.neon_dsn", ""
        )
        if not neon_dsn:
            _logger.info("ICC fallback poll skipped: Neon DSN not configured")
            return

        _logger.info("ICC fallback poll: checking Neon for unsynced records...")

        # TODO: Connect to Neon via psycopg2 using neon_dsn, query for
        # records where odoo_status IS NULL, and route them through the
        # webhook processor pipeline:
        #
        #   import psycopg2
        #   conn = psycopg2.connect(neon_dsn)
        #   cur = conn.cursor(cursor_factory=psycopg2.extras.RealDictCursor)
        #
        #   # Example: poll unsynced invoices
        #   cur.execute("""
        #       SELECT * FROM "NeonInvoices"
        #       WHERE "OdooStatus" IS NULL
        #       ORDER BY "InvoiceDate" ASC
        #       LIMIT 100
        #   """)
        #   for row in cur.fetchall():
        #       channel = self.search([('code', '=', 'invoice')], limit=1)
        #       controller._process_event(channel, 'invoice.polled', row, ...)
        #
        #   conn.close()

        _logger.info("ICC fallback poll completed")
