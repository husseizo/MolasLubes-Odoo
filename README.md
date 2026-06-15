# MolasLubes Monorepo

This repository is the working system around Molas LUBES operations across SAP Business One, SQL Server cache, Neon PostgreSQL, Odoo, Liqui Moly catalog flows, AutoHub/Germax enrichment, admin web, and supervisor mobile.

The center of gravity is the ASP.NET Core backend in `src/`, but the repo also contains the companion frontend and integration apps that talk to it.

## What Is In This Repo

### Core backend

- `src/MolasLubes.Api`
  ASP.NET Core API used by admin tools, mobile flows, sync triggers, auth, notifications, Liqui Moly stock, transfers, and replenishment.
- `src/MolasLubes.Application`
  DTOs and application contracts used by the API and infrastructure services.
- `src/MolasLubes.Domain`
  Domain entities for cache, Neon, orders, invoices, and related models.
- `src/MolasLubes.Infrastructure`
  SAP B1 DI API readers/writers, EF Core persistence, sync services, Quartz jobs, scrapers, notifications, and security services.

### Companion apps

- `molas-admin-web`
  Next.js admin frontend.
- `molas_supervisor_mobile`
  Flutter supervisor mobile app for alerts, approvals, and Liqui Moly replenishment workflows.
- `MolasLubesOdoo.Api`
  Smaller ASP.NET Core service focused on cache and Neon invoice-style flows.
- `odoo_addon/molas_sap_integration`
  Odoo addon that exposes REST endpoints for delivery, invoice, and payment ingestion.
- `sap-odoo-bridge`
  Node.js bridge for SAP Service Layer style real-time webhook forwarding to Odoo.

### Supporting material

- `tests/MolasLubes.Tests`
  Unit tests without the Windows-only SAP COM dependency.
- `docs`, `QUICK_START.md`, `ARCHITECTURE_*.md`, `*_SUMMARY.md`
  Project notes, roadmaps, and operational documents.

## High-Level Architecture

```text
SAP Business One
  |
  |  DI API readers/writers
  v
SQL Server cache databases
  - MolasCacheDb
  - Live2021CacheDb
  |
  |  EF Core + sync services
  v
Neon PostgreSQL databases
  - MolasLUBES
  - Parts_Catalog / AutoHub
  |
  |  outbound sync / API reads
  v
Odoo, Admin Web, Supervisor Mobile
```

There are really two main operating modes:

- `MolasLubes`
  The primary profile for Molas LUBES SAP, cache, Neon, Liqui Moly stock, deliveries, transfers, and replenishment.
- `AutoHub`
  A second profile used for AutoHub and Germax related inventory/catalog flows.

## How The Main Backend Works

The main app starts in [`src/MolasLubes.Api/Program.cs`](src/MolasLubes.Api/Program.cs).

On startup it does four big things:

1. Configures logging, auth, Swagger/OpenAPI, and JSON camelCase output.
2. Registers EF Core contexts for SQL Server and PostgreSQL.
3. Registers SAP DI API readers/writers, cache services, Neon sync services, Odoo push services, Liqui Moly services, notification services, and scrapers.
4. Registers Quartz jobs that keep the data moving in the background.

## Main Data Pipelines

### 1. SAP -> Cache

This is the first layer. SAP B1 is read through DI API services such as:

- `SapProductReader`
- `SapCustomerReader`
- `SapSalesOrderReader`
- `SapDeliveryReader`
- `SapInvoiceReader`
- `SapPaymentReader`

Those feeds are stored into SQL Server cache tables by services such as:

- `ProductCacheService`
- `CustomerCacheService`
- `DeliveryCacheService`
- `SalesOrderCacheService`
- `InvoiceCacheService`
- `PaymentCacheService`

This gives the system a local operational read model instead of hitting SAP for every request.

### 2. Cache -> Neon

The cache layer is then replicated into Neon PostgreSQL by services such as:

- `NeonCustomerSyncService`
- `NeonDeliverySyncService`
- `NeonInvoiceSyncService`
- `NeonPaymentSyncService`
- `NeonSalesOrderSyncService`
- `NeonSalesOrderLineSyncService`
- `ProductNeonSyncService`
- `PriceListNeonSyncService`

This is the second layer and acts as the cleaner integration/read layer for downstream systems.

### 3. Neon -> Odoo

Odoo-facing pushes are handled by:

- `OdooDeliveryPushService`
- `OdooInvoicePushService`
- `OdooPaymentPushService`

These are the final outbound bridge to Odoo.

## Liqui Moly Features

Liqui Moly is one of the most feature-rich parts of this repo.

### Catalog enrichment

Liqui Moly product metadata is scraped from the public catalog by:

- `LiquiMolyProductScraperService`
- `LiquiMolyCacheSyncService`
- `LiquiMolyNeonSyncService`
- `LiquiMolyProductScrapeJob`

This enriches article numbers with names, descriptions, barcodes, content sections, files, and overview metadata.

### Inventory and movement timeline

The Liqui Moly inventory APIs are driven mainly by:

- [`AdminLiquiMolyInventoryController`](src/MolasLubes.Api/Controllers/LiquiMoly/AdminLiquiMolyInventoryController.cs)
- [`SapLiquiMolyInventoryReader`](src/MolasLubes.Infrastructure/Integrations/SapB1/DiApi/SapLiquiMolyInventoryReader.cs)
- [`SapLiquiMolyDocumentReader`](src/MolasLubes.Infrastructure/Integrations/SapB1/DiApi/SapLiquiMolyDocumentReader.cs)

These power:

- stock list
- stock summary
- stock change polling
- movement timeline
- delivery aggregates
- document drilldown

The movement timeline currently supports major SAP document families including:

- `SO / ORDR`
- `DLV / ODLN`
- `TRQ / OWTQ`
- `TRF / OWTR`
- `GR / OIGN`
- `GI / OIGE`
- `INC / OINC`
- `IP / OIQR`

### Transfers and replenishment

Liqui Moly transfer and replenishment flows are handled by:

- `LiquiMolyTransferService`
- `LiquiMolyReplenishmentService`
- `LiquiMolyReplenishmentExecutionService`
- `SapInventoryTransferRequestWriter`
- `SapInterCompanySalesOrderWriter`
- `SapPurchaseOrderWriter`
- `SapGoodsReceiptWriter`
- `SapGoodsIssueWriter`

This area includes:

- demand analysis
- source mapping
- inter-company document creation
- transfer request creation
- warehouse document drilldown
- APNS push notifications for approval flows

## AutoHub and Germax

The second profile adds a separate catalog flow for AutoHub and Germax:

- `SapAutoHubSeedReader`
- `GermaxCacheSyncService`
- `GermaxAutoHubSyncService`
- `GermaxProductScraperService`
- `AutoHubSapSeedSyncJob`
- `GermaxProductEnrichmentJob`
- `GermaxRetryFailedJob`

These use:

- `Live2021CacheDbContext`
- `AutoHubDbContext`

## Security and Auth

The main backend uses two protection styles:

- API key security for protected admin/integration routes
- JWT auth for user/session-based flows

Relevant pieces:

- `ApiKeyAttribute`
- `ApiKeyOptions`
- `JwtService`
- `RefreshTokenStore`
- `SapUserAuthService`
- `LiquiMolyRoleService`

Push notifications use APNS via:

- `ApnsNotificationSender`
- `LiquiMolyPushNotificationService`

## Major Controllers

### General backend

- `AuthController`
- `AuthAltController`
- `CustomersController`
- `ProductsController`
- `SalesOrdersController`
- `InvoicesController`
- `StockController`
- `PaymentsCommandController`
- `NotificationsController`
- `AdminSyncController`
- `AdminItemsController`

### Liqui Moly

- `LiquiMolyProductsController`
- `LiquiMolyReplenishmentController`
- `AdminLiquiMolyInventoryController`
- `AdminLiquiMolyDocumentsController`
- `AdminLiquiMolyTransfersController`
- `AdminLiquiMolyReplenishmentController`

### AutoHub / Neon

- `AutoHubAdminController`
- `AutoHubGermaxProductsController`
- `AdminNeonSyncController`
- `NeonProductsController`
- `NeonPriceListsController`

## Major Quartz Jobs

These jobs keep the system alive in the background.

### SAP -> Cache

- `ProductFullSyncJob`
- `CustomerDeltaSyncJob`
- `SalesOrderSyncJob`
- `SapOpenOrdersSyncJob`
- `DeliveryDeltaSyncJob`
- `InvoiceSyncJob`
- `PaymentSyncJob`

### Cache -> Neon

- `NeonCustomerSyncJob`
- `NeonDeliverySyncJob`
- `NeonInvoiceSyncJob`
- `NeonPaymentSyncJob`
- `NeonProductDeltaSyncJob`
- `NeonSalesOrderSyncJob`
- `NeonSalesOrderLineSyncJob`
- `NeonPriceListSyncJob`

### Neon -> Odoo

- `OdooDeliveryPushJob`
- `OdooInvoicePushJob`
- `OdooPaymentPushJob`

### Catalog / enrichment / profile B

- `LiquiMolyProductScrapeJob`
- `AutoHubSapSeedSyncJob`
- `GermaxProductEnrichmentJob`
- `GermaxRetryFailedJob`

### Background service

- `NeonKeepAliveService`

## Request Flow Examples

### Standard sync flow

```text
SAP document
  -> SAP reader
  -> cache service writes SQL Server cache
  -> Neon sync service copies to PostgreSQL
  -> Odoo push service sends to Odoo
```

### Liqui Moly stock screen

```text
Supervisor mobile / admin web
  -> /api/admin/liquimoly/inventory/*
  -> SapLiquiMolyInventoryReader
  -> SAP stock + movement documents
  -> optional cache metadata merge from CacheLiquiMolyProducts
```

### Liqui Moly replenishment approval flow

```text
mobile approval
  -> replenishment service
  -> SAP document creation
  -> transfer / request / SO / PO / GR / GI chain as needed
  -> APNS notification + timeline drilldown
```

## Databases

### SQL Server

- `MolasCacheDb`
  Primary cache/read model for Molas LUBES.
- `Live2021CacheDb`
  AutoHub-side cache database.

### PostgreSQL / Neon

- `NeonDbContext`
  Main Neon database for Molas flows.
- `AutoHubDbContext`
  AutoHub / Germax Neon database.

### SAP

SAP is accessed through the SAP Business One DI API and remains the system of record for core ERP documents.

## Running The Main API

### Prerequisites

- Windows
- .NET 10 SDK
- SQL Server access
- PostgreSQL / Neon access
- SAP Business One DI API installed

### Start the main API

```powershell
dotnet run --project src/MolasLubes.Api/MolasLubes.Api.csproj
```

### Run tests

```powershell
dotnet test tests/MolasLubes.Tests/MolasLubes.Tests.csproj
```

## Important Operational Notes

- `src/MolasLubes.Api/Program.cs` is the single best entry point for understanding what the app does.
- Most business movement in this system is asynchronous and job-driven, not request-driven.
- Liqui Moly stock screens mix live SAP reads with cache metadata.
- SAP access is Windows-only because of COM-based DI API usage.
- Some areas are profile-specific. `MolasLubes` and `AutoHub` do not use the same DB pair.

## Security Note

This repository should be treated as sensitive operational code.

- Do not commit new secrets, tokens, private keys, or production connection strings.
- If any real secret has been committed at any point, rotate it.
- Prefer environment variables, user secrets, or secret managers for deployments.

## Where To Start If You Are New

If you want to understand the system fast, read in this order:

1. [`src/MolasLubes.Api/Program.cs`](src/MolasLubes.Api/Program.cs)
2. [`src/MolasLubes.Infrastructure/Persistence/MolasCacheDbContext.cs`](src/MolasLubes.Infrastructure/Persistence/MolasCacheDbContext.cs)
3. [`src/MolasLubes.Infrastructure/Persistence/NeonDbContext.cs`](src/MolasLubes.Infrastructure/Persistence/NeonDbContext.cs)
4. One controller from the area you care about
5. The matching service or SAP reader/writer
6. The matching Quartz job if the feature is sync-driven

Good first files by topic:

- inventory: [`SapLiquiMolyInventoryReader`](src/MolasLubes.Infrastructure/Integrations/SapB1/DiApi/SapLiquiMolyInventoryReader.cs)
- replenishment: [`LiquiMolyReplenishmentService`](src/MolasLubes.Infrastructure/Services/LiquiMolyReplenishment/LiquiMolyReplenishmentService.cs)
- stock sync: [`DeliveryDeltaSyncJob`](src/MolasLubes.Infrastructure/Scheduling/Jobs/DeliveryDeltaSyncJob.cs)
- Odoo push: [`OdooDeliveryPushService`](src/MolasLubes.Infrastructure/Services/Sync/OdooDeliveryPushService.cs)
- product scrape: [`LiquiMolyProductScraperService`](src/MolasLubes.Infrastructure/Integrations/LiquiMoly/LiquiMolyProductScraperService.cs)
