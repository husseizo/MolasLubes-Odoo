using Microsoft.EntityFrameworkCore;
using MolasLubes.Api.Security;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using MolasLubes.Infrastructure.Common;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Scheduling.Jobs;
using MolasLubes.Infrastructure.Services.Caching;
using MolasLubes.Infrastructure.Services.Finance;
using MolasLubes.Infrastructure.Services.Orders;
using MolasLubes.Infrastructure.Services.Pricing;
using MolasLubes.Infrastructure.Services.Stock;
using MolasLubes.Infrastructure.Services.Sync;
using MolasLubes.Infrastructure.Services.Background;
using MolasLubes.Infrastructure.Security;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Integrations.Meguin;
using MolasLubes.Infrastructure.Integrations.Germax;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(); // enables running as a Windows Service (SCM integration)

// =====================================================
// 📄 LOGGING — SERILOG (Console + File)
// =====================================================
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)   // allow appsettings overrides

    // App-level minimum
    .MinimumLevel.Information()

    // Suppress noisy framework namespaces (keep at Warning only)
    .MinimumLevel.Override("Microsoft",                       LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore",            LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore",   LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient",      LogEventLevel.Warning)
    .MinimumLevel.Override("Quartz",                          LogEventLevel.Warning)

    .Enrich.FromLogContext()

    // ── Console ────────────────────────────────────────────────────────
    .WriteTo.Console(
        outputTemplate:
            "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
        theme: AnsiConsoleTheme.Code)

    // ── Rolling file — C:\Dev\Sapscrapodoo\ ────────────────────────────
    // File: molaslubes-20260303.log  (new file each day)
    // Retention: 31 days; max 50 MB per file before rolling to the next
    .WriteTo.File(
        path: @"C:\Dev\Sapscrapodoo\molaslubes-.log",
        rollingInterval:        RollingInterval.Day,
        retainedFileCountLimit: 31,
        fileSizeLimitBytes:     50_000_000,
        rollOnFileSizeLimit:    true,
        shared:                 true,           // safe for multi-process write
        outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"));

// =====================================================
// 🔧 SYNC SETTINGS
// =====================================================
builder.Services.Configure<SyncSettings>(
    builder.Configuration.GetSection("SyncSettings"));

var syncSettings = builder.Configuration
    .GetSection("SyncSettings")
    .Get<SyncSettings>() ?? new SyncSettings();

// =====================================================
// 🔐 API SECURITY
// =====================================================
builder.Services.Configure<ApiKeyOptions>(
    builder.Configuration.GetSection("ApiSecurity"));

builder.Services.AddScoped<ApiKeyAttribute>();

// =====================================================
// CORE
// =====================================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOpenApi();

// =====================================================
// DATABASES — PROFILE A (Molas_Lubes_LTD)
// =====================================================
builder.Services.AddDbContext<MolasCacheDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("MolasCacheDb")));

builder.Services.AddDbContext<NeonDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("NeonDb"),
        npgsql =>
        {
            npgsql.MigrationsAssembly("MolasLubes.Infrastructure");
            npgsql.CommandTimeout(120);
            npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        }));

// =====================================================
// DATABASES — PROFILE B (MOLAS_Live_2021 / AutoHub)
// =====================================================
builder.Services.AddDbContext<Live2021CacheDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration["IntegrationProfiles:Profiles:AutoHub:ConnectionStrings:CacheDb"],
        sql =>
        {
            sql.MigrationsAssembly("MolasLubes.Infrastructure");
            sql.MigrationsHistoryTable("__EFMigrationsHistory_Live2021Cache");
        }));

builder.Services.AddDbContext<AutoHubDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration["IntegrationProfiles:Profiles:AutoHub:ConnectionStrings:NeonDb"],
        npgsql =>
        {
            npgsql.MigrationsAssembly("MolasLubes.Infrastructure");
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory_AutoHub");
            npgsql.CommandTimeout(120);
            npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
        }));

// =====================================================
// INTEGRATION PROFILES (Profile A + Profile B)
// =====================================================
builder.Services.Configure<IntegrationProfilesOptions>(
    builder.Configuration.GetSection(IntegrationProfilesOptions.SectionName));

// =====================================================
// GERMAX SCRAPER SETTINGS
// =====================================================
builder.Services.Configure<GermaxScraperSettings>(
    builder.Configuration.GetSection(GermaxScraperSettings.SectionName));

// =====================================================
// SAP SETTINGS (legacy — Profile A backward compat)
// =====================================================
builder.Services.Configure<SapSettings>(
    builder.Configuration.GetSection("SAP"));

// =====================================================
// SAP DI SERVICES — PROFILE A
// =====================================================
builder.Services.AddSingleton<SapDiApiConnection>();

builder.Services.AddScoped<SapProductReader>();
builder.Services.AddScoped<SapCustomerReader>();
builder.Services.AddScoped<SapSalesOrderReader>();
builder.Services.AddScoped<SapDeliveryReader>();
builder.Services.AddScoped<SapInvoiceReader>();
builder.Services.AddScoped<SapPaymentReader>();
builder.Services.AddScoped<SapPricingReader>();
builder.Services.AddScoped<SapCreditMemoReader>();

builder.Services.AddScoped<SapCustomerWriter>();
builder.Services.AddScoped<SapSalesOrderCreator>();
builder.Services.AddScoped<SapSalesOrderCanceler>();
builder.Services.AddScoped<SapQuotationConverter>();
builder.Services.AddScoped<SapInvoiceWriter>();
builder.Services.AddScoped<SapCreditMemoWriter>();
builder.Services.AddScoped<SapPaymentWriter>();
builder.Services.AddScoped<SapPaymentChannelReader>();

builder.Services.AddScoped<OdooPushResultHandler>();

builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.Idempotency.IdempotencyService>();

// =====================================================
// CACHE SERVICES
// =====================================================
builder.Services.AddScoped<ProductCacheService>();
builder.Services.AddScoped<CustomerCacheService>();
builder.Services.AddScoped<DeliveryCacheService>();
builder.Services.AddScoped<SalesOrderCacheService>();
builder.Services.AddScoped<InvoiceCacheService>();
builder.Services.AddScoped<PaymentCacheService>();

// =====================================================
// DOMAIN SERVICES
// =====================================================
builder.Services.AddScoped<CustomerPricingService>();
builder.Services.AddScoped<CustomerCreditService>();
builder.Services.AddScoped<OrderLifecycleService>();
builder.Services.AddScoped<InvoiceBalanceService>();
builder.Services.AddScoped<SalesOrderStatusService>();
builder.Services.AddScoped<StockReservationService>();
builder.Services.AddScoped<ReservationCommitService>();
builder.Services.AddScoped<CancelSalesOrderService>();

// =====================================================
// AUTOHUB SERVICES — PROFILE B
// =====================================================
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapAutoHubSeedReader>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.Sync.GermaxCacheSyncService>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.Sync.GermaxAutoHubSyncService>();

builder.Services.AddHttpClient<GermaxProductScraperService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    client.Timeout = TimeSpan.FromSeconds(
        config.GetValue("GermaxScraper:RequestTimeoutSeconds", 30));

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/122 Safari/537.36");

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "Accept-Language", "en-US,en;q=0.9");
});

// =====================================================
// NEON SYNC SERVICES
// =====================================================
builder.Services.AddScoped<NeonCustomerSyncService>();
builder.Services.AddScoped<NeonDeliverySyncService>();
builder.Services.AddScoped<NeonInvoiceSyncService>();
builder.Services.AddScoped<NeonPaymentSyncService>();
builder.Services.AddScoped<NeonSalesOrderSyncService>();
builder.Services.AddScoped<NeonSalesOrderLineSyncService>();
builder.Services.AddScoped<ProductNeonSyncService>();
builder.Services.AddScoped<PriceListNeonSyncService>();

builder.Services.AddHostedService<NeonKeepAliveService>();

// =====================================================
// LIQUI-MOLY SCRAPER
// =====================================================
builder.Services.Configure<LiquiMolyScraperSettings>(
    builder.Configuration.GetSection("LiquiMolyScraper"));

builder.Services.AddHttpClient<LiquiMolyProductScraperService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    client.Timeout = TimeSpan.FromSeconds(
        config.GetValue("LiquiMolyScraper:RequestTimeoutSeconds", 120));

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/122 Safari/537.36");

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "Accept-Language", "en-US,en;q=0.9");

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "Origin", "https://www.liqui-moly.com");
});

builder.Services.AddScoped<MolasLubes.Infrastructure.Services.Sync.LiquiMolyCacheSyncService>();
builder.Services.AddScoped<MolasLubes.Infrastructure.Services.Sync.LiquiMolyNeonSyncService>();

// =====================================================
// MEGUIN SCRAPER
// =====================================================
builder.Services.Configure<MeguinScraperSettings>(
    builder.Configuration.GetSection("MeguinScraper"));

builder.Services.AddHttpClient<MeguinProductScraperService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    client.Timeout = TimeSpan.FromSeconds(
        config.GetValue("MeguinScraper:RequestTimeoutSeconds", 120));

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/122 Safari/537.36");

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "Accept-Language", "en-US,en;q=0.9");

    client.DefaultRequestHeaders.TryAddWithoutValidation(
        "Origin", "https://www.meguin.com");
});

// =====================================================
// ODOO PUSH SERVICES
// =====================================================
builder.Services.AddHttpClient<OdooApiClient>(client =>
{
    var baseUrl = builder.Configuration["OdooApi:BaseUrl"] ?? "";
    if (!string.IsNullOrWhiteSpace(baseUrl))
        client.BaseAddress = new Uri(baseUrl);

    var apiKey = builder.Configuration["OdooApi:ApiKey"] ?? "";
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-API-KEY", apiKey);
});

builder.Services.AddScoped<OdooDeliveryPushService>();
builder.Services.AddScoped<OdooInvoicePushService>();
builder.Services.AddScoped<OdooPaymentPushService>();

// =====================================================
// QUARTZ JOB REGISTRATION
// =====================================================
builder.Services.AddTransient<ProductFullSyncJob>();
builder.Services.AddTransient<CustomerDeltaSyncJob>();
builder.Services.AddTransient<CustomerFullSyncJob>();
builder.Services.AddTransient<SalesOrderSyncJob>();
builder.Services.AddTransient<MolasLubes.Infrastructure.Scheduling.Jobs.SapOpenOrdersSyncJob>();
builder.Services.AddTransient<InvoiceSyncJob>();
builder.Services.AddTransient<InvoiceFullSyncJob>();
builder.Services.AddTransient<PaymentSyncJob>();
builder.Services.AddTransient<DeliveryDeltaSyncJob>();
builder.Services.AddTransient<OdooDeliveryPushJob>();
builder.Services.AddTransient<OdooInvoicePushJob>();
builder.Services.AddTransient<OdooPaymentPushJob>();

builder.Services.AddTransient<NeonCustomerSyncJob>();
builder.Services.AddTransient<NeonProductDeltaSyncJob>();
builder.Services.AddTransient<NeonInvoiceSyncJob>();
builder.Services.AddTransient<NeonPaymentSyncJob>();
builder.Services.AddTransient<NeonDeliverySyncJob>();
builder.Services.AddTransient<NeonSalesOrderSyncJob>();
builder.Services.AddTransient<NeonSalesOrderLineSyncJob>();
builder.Services.AddTransient<NeonPriceListSyncJob>();
builder.Services.AddTransient<LiquiMolyProductScrapeJob>();
builder.Services.AddTransient<QuotationToSalesOrderJob>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Scheduling.Jobs.AutoHubSapSeedSyncJob>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Scheduling.Jobs.GermaxProductEnrichmentJob>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Scheduling.Jobs.GermaxRetryFailedJob>();

// =====================================================
// QUARTZ CONFIGURATION
// =====================================================
builder.Services.AddQuartz(q =>
{
    q.UseDefaultThreadPool(tp =>
    {
        tp.MaxConcurrency = 1; // single pipeline execution
    });

    void RegisterJob<T>(string name, string cron) where T : IJob
    {
        var key = new JobKey(name);

        q.AddJob<T>(o => o.WithIdentity(key));

        q.AddTrigger(t => t
            .ForJob(key)
            .WithIdentity($"{name}-trigger")
            .WithCronSchedule(cron));

        
    }

    // =========================
    // SAP → CACHE (first layer)
    // =========================
    RegisterJob<ProductFullSyncJob>("ProductFullSyncJob", "0 0 */6 ? * *"); // every 6 hours
    RegisterJob<CustomerDeltaSyncJob>("CustomerDeltaSyncJob", "0 */5 * ? * *");
    RegisterJob<SalesOrderSyncJob>("SalesOrderSyncJob", "10 */5 * ? * *");
    RegisterJob<MolasLubes.Infrastructure.Scheduling.Jobs.SapOpenOrdersSyncJob>("SapOpenOrdersSyncJob", "20 */5 * ? * *"); // every 5 min — open orders
    RegisterJob<QuotationToSalesOrderJob>("QuotationToSalesOrderJob", "0 */2 * ? * *"); // every 2 min — convert open OQUT → ORDR
    RegisterJob<DeliveryDeltaSyncJob>("DeliveryDeltaSyncJob", "0/10 * * ? * *"); // every 10s — SAP→Cache (delivery layer 1)


    if (syncSettings.EnableInvoiceCacheSync)
        RegisterJob<InvoiceSyncJob>("InvoiceSyncJob", "3/10 * * ? * *"); // every 10s — SAP→Cache (invoice layer 1)

    if (syncSettings.EnablePaymentCacheSync)
        RegisterJob<PaymentSyncJob>("PaymentSyncJob", "6/10 * * ? * *"); // every 10s — SAP→Cache (payment layer 1)

    // =========================
    // CACHE → NEON (second layer)
    // =========================
    if (syncSettings.EnableNeonCustomerSync)
        RegisterJob<NeonCustomerSyncJob>("NeonCustomerSyncJob", "5 */5 * ? * *"); // every 5 min (customers change infrequently)

    if (syncSettings.EnableNeonInvoiceSync)
        RegisterJob<NeonInvoiceSyncJob>("NeonInvoiceSyncJob", "4/10 * * ? * *"); // every 10s — Cache→Neon (invoice layer 2)

    if (syncSettings.EnableNeonPaymentSync)
        RegisterJob<NeonPaymentSyncJob>("NeonPaymentSyncJob", "7/10 * * ? * *"); // every 10s — Cache→Neon (payment layer 2)

    RegisterJob<NeonDeliverySyncJob>("NeonDeliverySyncJob", "1/10 * * ? * *"); // every 10s — Cache→Neon (delivery layer 2)

    if (syncSettings.EnableOdooDeliveryPush)
        RegisterJob<OdooDeliveryPushJob>("OdooDeliveryPushJob", "2/10 * * ? * *"); // every 10s — Neon→Odoo (delivery layer 3)

    if (syncSettings.EnableOdooInvoicePush)
        RegisterJob<OdooInvoicePushJob>("OdooInvoicePushJob", "5/10 * * ? * *"); // every 10s — Neon→Odoo (invoice layer 3)

    if (syncSettings.EnableOdooPaymentPush)
        RegisterJob<OdooPaymentPushJob>("OdooPaymentPushJob", "8/10 * * ? * *"); // every 10s — Neon→Odoo (payment layer 3)

    RegisterJob<NeonProductDeltaSyncJob>("NeonProductDeltaSyncJob", "55 */10 * ? * *");
    RegisterJob<NeonSalesOrderSyncJob>(
          "NeonSalesOrderSyncJob",
          "15 */5 * ? * *"); // every 5 minutes

    RegisterJob<NeonSalesOrderLineSyncJob>(
        "NeonSalesOrderLineSyncJob",
        "25 */5 * ? * *"); // every 5 minutes

    // Durable manual-only full sync
    q.AddJob<CustomerFullSyncJob>(opts =>
        opts.WithIdentity("CustomerFullSyncJob")
            .StoreDurably());

    q.AddJob<InvoiceFullSyncJob>(opts =>
        opts.WithIdentity("InvoiceFullSyncJob")
            .StoreDurably());

    // Price list sync: every 6 hours (also manually triggerable via admin endpoint)
    RegisterJob<NeonPriceListSyncJob>("NeonPriceListSyncJob", "0 30 */6 ? * *");

    // Liqui-Moly product catalog scrape: once daily at 02:00 UTC
    // Also manually triggerable via POST /api/admin/liquimoly/scrape
    q.AddJob<LiquiMolyProductScrapeJob>(opts =>
        opts.WithIdentity("LiquiMolyProductScrapeJob")
            .StoreDurably());

    q.AddTrigger(t => t
        .ForJob(new JobKey("LiquiMolyProductScrapeJob"))
        .WithIdentity("LiquiMolyProductScrapeJob-trigger")
        .WithCronSchedule("0 0 2 ? * *")); // daily at 02:00 UTC

    // =========================
    // AUTOHUB — Profile B
    // =========================
    q.AddJob<MolasLubes.Infrastructure.Scheduling.Jobs.AutoHubSapSeedSyncJob>(opts =>
        opts.WithIdentity("AutoHubSapSeedSyncJob")
            .StoreDurably());

    q.AddTrigger(t => t
        .ForJob(new JobKey("AutoHubSapSeedSyncJob"))
        .WithIdentity("AutoHubSapSeedSyncJob-trigger")
        .WithCronSchedule("0 0 */6 ? * *")); // every 6 hours

    // Germax enrichment: nightly at 01:30 UTC (after seed sync at 00:00)
    // StoreDurably so it can also be triggered via POST /api/admin/autohub/germax/scrape
    q.AddJob<MolasLubes.Infrastructure.Scheduling.Jobs.GermaxProductEnrichmentJob>(opts =>
        opts.WithIdentity("GermaxProductEnrichmentJob")
            .StoreDurably());

    q.AddTrigger(t => t
        .ForJob(new JobKey("GermaxProductEnrichmentJob"))
        .WithIdentity("GermaxProductEnrichmentJob-trigger")
        .WithCronSchedule("0 30 1 ? * *")); // nightly at 01:30 UTC

    // Germax retry: 09:00 and 21:00 UTC (avoids overlap with seed sync at 00/06/12/18)
    // StoreDurably so it can also be triggered via POST /api/admin/autohub/germax/retry-failed
    q.AddJob<MolasLubes.Infrastructure.Scheduling.Jobs.GermaxRetryFailedJob>(opts =>
        opts.WithIdentity("GermaxRetryFailedJob")
            .StoreDurably());

    q.AddTrigger(t => t
        .ForJob(new JobKey("GermaxRetryFailedJob"))
        .WithIdentity("GermaxRetryFailedJob-trigger")
        .WithCronSchedule("0 0 9,21 ? * *")); // 09:00 and 21:00 UTC
});

builder.Services.AddQuartzHostedService(o =>
{
    o.WaitForJobsToComplete = true;
});

// =====================================================
// BUILD APP
// =====================================================
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var sqlConn = builder.Configuration.GetConnectionString("MolasCacheDb");
    if (!string.IsNullOrWhiteSpace(sqlConn) && !sqlConn.StartsWith("CHANGE_ME"))
    {
        scope.ServiceProvider
            .GetRequiredService<MolasCacheDbContext>()
            .Database.Migrate();
    }
    else
    {
        Log.Warning("MolasCacheDb connection string is not configured — skipping SQL Server migration.");
    }

    var neonConn = builder.Configuration.GetConnectionString("NeonDb");
    if (!string.IsNullOrWhiteSpace(neonConn) && !neonConn.StartsWith("CHANGE_ME"))
    {
        scope.ServiceProvider
            .GetRequiredService<NeonDbContext>()
            .Database.Migrate();
    }
    else
    {
        Log.Warning("NeonDb connection string is not configured — skipping PostgreSQL migration.");
    }

    // Profile B — MOLAS_Live_2021_Cache (SQL Server)
    var live2021CacheConn = builder.Configuration[
        "IntegrationProfiles:Profiles:AutoHub:ConnectionStrings:CacheDb"];
    if (!string.IsNullOrWhiteSpace(live2021CacheConn) && !live2021CacheConn.StartsWith("CHANGE_ME"))
    {
        scope.ServiceProvider
            .GetRequiredService<Live2021CacheDbContext>()
            .Database.Migrate();
    }
    else
    {
        Log.Warning("AutoHub CacheDb connection string is not configured — skipping Live2021Cache migration.");
    }

    // Profile B — MolasAutoHub (Neon/PostgreSQL)
    var autoHubNeonConn = builder.Configuration[
        "IntegrationProfiles:Profiles:AutoHub:ConnectionStrings:NeonDb"];
    if (!string.IsNullOrWhiteSpace(autoHubNeonConn) && !autoHubNeonConn.StartsWith("CHANGE_ME"))
    {
        scope.ServiceProvider
            .GetRequiredService<AutoHubDbContext>()
            .Database.Migrate();
    }
    else
    {
        Log.Warning("AutoHub NeonDb connection string is not configured — skipping MolasAutoHub migration.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.MapControllers();

// =====================================================
// DEBUG
// =====================================================
app.MapGet("/debug/cache-counts", async (MolasCacheDbContext db) =>
{
    return new
    {
        Products = await db.CacheProducts.CountAsync(),
        Customers = await db.CacheCustomers.CountAsync(),
        SalesOrders = await db.CacheSalesOrders.CountAsync(),
        Deliveries = await db.CacheDeliveries.CountAsync(),
        Invoices = await db.CacheInvoices.CountAsync(),
        InvoiceLines = await db.CacheInvoiceLines.CountAsync(),
        Payments = await db.CachePayment.CountAsync()
    };
});

app.MapGet("/", () => Results.Ok("MolasLubes API is running 🚀"));

app.Run();