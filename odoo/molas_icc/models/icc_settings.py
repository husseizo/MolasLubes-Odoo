from odoo import fields, models


class IccSettings(models.TransientModel):
    """ICC global configuration exposed in Settings > Integration Control Centre."""

    _inherit = "res.config.settings"

    # ── global toggle ────────────────────────────────────
    icc_webhooks_enabled = fields.Boolean(
        string="Enable Webhooks",
        config_parameter="molas_icc.webhooks_enabled",
        default=True,
        help="Master switch — when disabled, all inbound webhooks return 503.",
    )

    # ── authentication ───────────────────────────────────
    icc_default_auth_method = fields.Selection(
        [
            ("api_key", "API Key"),
            ("hmac", "HMAC-SHA256 Signature"),
        ],
        string="Default Auth Method",
        config_parameter="molas_icc.default_auth_method",
        default="api_key",
    )

    # ── log retention ────────────────────────────────────
    icc_log_retention_days = fields.Integer(
        string="Log Retention (days)",
        config_parameter="molas_icc.log_retention_days",
        default=90,
        help="Event logs older than this are purged by the cleanup cron.",
    )

    # ── fallback polling ─────────────────────────────────
    icc_fallback_cron_enabled = fields.Boolean(
        string="Enable Fallback Polling",
        config_parameter="molas_icc.fallback_cron_enabled",
        default=True,
        help="Safety-net cron that polls Neon for records missed by webhooks.",
    )
    icc_fallback_interval_minutes = fields.Integer(
        string="Fallback Interval (min)",
        config_parameter="molas_icc.fallback_interval_minutes",
        default=30,
    )

    # ── Neon connection ──────────────────────────────────
    icc_neon_dsn = fields.Char(
        string="Neon Database DSN",
        config_parameter="molas_icc.neon_dsn",
        help="PostgreSQL connection string to the Neon intermediary DB.",
    )
