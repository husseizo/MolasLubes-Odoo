-- =============================================================================
-- Liqui Moly Replenishment — Cache Table DDL
-- Run against the MolasLubes bridge/cache database before first API call.
-- Safe to re-run: CREATE TABLE IF NOT EXISTS + idempotent index creation.
-- =============================================================================

-- ── Headers ──────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS CacheLiquiMolyReplenishmentRequests (
    Id              INTEGER      PRIMARY KEY AUTOINCREMENT,
    RequestRef      TEXT         NOT NULL UNIQUE,     -- e.g. "LM-2026-001"
    Status          TEXT         NOT NULL DEFAULT 'DRAFT',
    -- DRAFT | PENDING_APPROVAL | APPROVED | REJECTED
    -- EXECUTING | EXECUTED | PARTIAL | FAILED

    -- Profiles and warehouses
    SourceProfile   TEXT         NOT NULL,
    TargetProfile   TEXT         NOT NULL,
    SourceWarehouse TEXT         NOT NULL,
    TargetWarehouse TEXT         NOT NULL,

    -- SAP document references (populated after execution)
    TransferRef          TEXT    NULL,
    GoodsIssueDocEntry   INTEGER NULL,
    GoodsIssueDocNum     INTEGER NULL,
    GoodsReceiptDocEntry INTEGER NULL,
    GoodsReceiptDocNum   INTEGER NULL,

    -- Actor trail
    RequestedBySapUser TEXT     NOT NULL,
    SubmittedBySapUser TEXT     NULL,
    ApprovedBySapUser  TEXT     NULL,
    RejectedBySapUser  TEXT     NULL,
    ExecutedBySapUser  TEXT     NULL,

    -- Timestamps (ISO-8601 UTC)
    CreatedAt    TEXT  NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    SubmittedAt  TEXT  NULL,
    ApprovedAt   TEXT  NULL,
    RejectedAt   TEXT  NULL,
    ExecutedAt   TEXT  NULL,

    -- Narrative
    Comments        TEXT NULL,
    RejectionReason TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_CLMRRequests_Status
    ON CacheLiquiMolyReplenishmentRequests (Status);

CREATE INDEX IF NOT EXISTS IX_CLMRRequests_CreatedAt
    ON CacheLiquiMolyReplenishmentRequests (CreatedAt DESC);

-- ── Lines ─────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS CacheLiquiMolyReplenishmentRequestLines (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    RequestRef      TEXT    NOT NULL
        REFERENCES CacheLiquiMolyReplenishmentRequests(RequestRef)
        ON DELETE CASCADE,

    -- Item identity
    ArticleNumber    TEXT NOT NULL,
    SourceItemCode   TEXT NOT NULL,   -- MolasLubes internal code (e.g. LUB100001)
    TargetItemCode   TEXT NOT NULL,   -- AutoHub code = article number (e.g. 3682)
    ItemDescription  TEXT NOT NULL,

    -- Demand snapshot at draft time
    CurrentStock          REAL NOT NULL DEFAULT 0,
    CurrentStockTarget    REAL NOT NULL DEFAULT 0,
    AvailableSupplierStock REAL NOT NULL DEFAULT 0,
    QtySold30d            REAL NOT NULL DEFAULT 0,
    QtySold60d            REAL NOT NULL DEFAULT 0,
    QtySold90d            REAL NOT NULL DEFAULT 0,
    AvgDailySales30d      REAL NOT NULL DEFAULT 0,
    DaysOfStock           REAL NOT NULL DEFAULT 0,
    TrendCategory         TEXT NOT NULL DEFAULT 'Normal',
    -- Normal | FastMoving | SlowMoving | DeadStock | Inactive | LowStock

    Priority              INTEGER NOT NULL DEFAULT 3,

    -- Quantities
    SuggestedQty     REAL NOT NULL DEFAULT 0,
    ApprovedQty      REAL NULL,   -- NULL = accept suggested; set if supervisor overrides

    -- Execution outcome
    ExecutionStatus  TEXT NOT NULL DEFAULT 'PENDING',
    -- PENDING | EXECUTED | GI_ISSUED | FAILED
    ExecutionMessage TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_CLMRLines_RequestRef
    ON CacheLiquiMolyReplenishmentRequestLines (RequestRef);
