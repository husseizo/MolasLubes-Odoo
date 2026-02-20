{
    "name": "Molas Integration Control Centre",
    "version": "17.0.1.0.0",
    "category": "Technical",
    "summary": "Central hub for managing SAP/Neon webhook integrations",
    "description": """
        Molas ICC — Integration Control Centre
        =======================================
        * Webhook endpoints for receiving SAP/Neon pushes
        * Channel-based endpoint registry (enable / disable per document type)
        * Full audit trail of every inbound and outbound event
        * Dashboard with health indicators and throughput graphs
        * API-key authentication per channel
        * Fallback cron for catching missed events
        * Retry wizard for failed events
    """,
    "author": "Molas Lubes",
    "website": "https://molaslubes.com",
    "license": "LGPL-3",
    "depends": [
        "base",
        "account",
        "stock",
        "sale",
    ],
    "data": [
        # Security
        "security/icc_security.xml",
        "security/ir.model.access.csv",
        # Views
        "views/icc_channel_views.xml",
        "views/icc_event_log_views.xml",
        "views/icc_dashboard.xml",
        "views/icc_settings_views.xml",
        "views/menus.xml",
        # Data
        "data/icc_channels_data.xml",
        "data/ir_cron_data.xml",
    ],
    "installable": True,
    "application": True,
    "auto_install": False,
}
