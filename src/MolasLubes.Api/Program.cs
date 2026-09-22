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
using MolasLubes.Infrastructure.Services.Backfill;
using MolasLubes.Infrastructure.Services.LiquiMolyTransfers;
using MolasLubes.Infrastructure.Services.LiquiMolyReplenishment;
using MolasLubes.Infrastructure.Services.Notifications;
using MolasLubes.Infrastructure.Services.Background;
using MolasLubes.Infrastructure.Security;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Integrations.Meguin;
using MolasLubes.Infrastructure.Integrations.Germax;
using MolasLubes.Infrastructure.Integrations.TantivyScraper;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using Microsoft.OpenApi;
using Quartz;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using MolasLubes.Api.Utilities;

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

var quartzMaxConcurrency = Math.Max(
    1,
    builder.Configuration.GetValue<int?>("QuartzConcurrency:MaxConcurrency") ?? 1);

// =====================================================
// 🔐 API SECURITY
// =====================================================
builder.Services.Configure<ApiKeyOptions>(
    builder.Configuration.GetSection("ApiSecurity"));

builder.Services.AddScoped<ApiKeyAttribute>();

// ── Internal user auth (opaque bearer tokens) ─────────────────────────
builder.Services.AddScoped<MolasLubes.Infrastructure.Security.InternalUserService>();
builder.Services.AddScoped<MolasLubes.Infrastructure.Security.InternalTokenService>();
builder.Services.AddScoped<MolasLubes.Infrastructure.Security.BrandRoleResolver>();
builder.Services.AddScoped<MolasLubes.Api.Security.BearerTokenAttribute>();

// =====================================================
// 🔐 JWT AUTHENTICATION (legacy — superseded by bearer tokens)
// =====================================================
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<RefreshTokenStore>();
builder.Services.AddScoped<SapUserAuthService>();

var jwtSecret = builder.Configuration["Jwt:Secret"];
if (!string.IsNullOrWhiteSpace(jwtSecret) && jwtSecret.Length >= 32)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "MolasLubes.Api",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "MolasLubes.Clients",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                {
                    context.Response.Headers.Append("Token-Expired", "true");
                }
                return Task.CompletedTask;
            }
        };
    });

    builder.Services.AddAuthorization();
}

// =====================================================
// CORE
// =====================================================
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Configure JSON serialization for camelCase (required by Flutter frontend)
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = false;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    const string apiKeyScheme = "ApiKey";

    options.AddSecurityDefinition(apiKeyScheme, new OpenApiSecurityScheme
    {
        Description = "Enter the API key for protected endpoints using the X-Api-Key header.",
        Type = SecuritySchemeType.ApiKey,
        Name = "X-Api-Key",
        In = ParameterLocation.Header
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference(apiKeyScheme, document, null),
            new List<string>()
        }
    });
});


builder.Services.AddOpenApi();

// =====================================================
// DATABASES — PROFILE A (Molas_Lubes_LTD)
// =====================================================
builder.Services.AddDbContext<MolasCacheDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("MolasCacheDb"))
        .ConfigureWarnings(w =>
            w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddDbContext<NeonDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("NeonDb"),
        npgsql =>
        {
            npgsql.MigrationsAssembly("MolasLubes.Infrastructure");
            npgsql.CommandTimeout(120);
            npgsql.ExecutionStrategy(c => new NeonRetryStrategy(c, maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(60)));
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
            npgsql.ExecutionStrategy(c => new NeonRetryStrategy(c, maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(60)));
        }));

// =====================================================
// INTEGRATION PROFILES (Profile A + Profile B)
// =====================================================
builder.Services.Configure<IntegrationProfilesOptions>(
    builder.Configuration.GetSection(IntegrationProfilesOptions.SectionName));

// =====================================================
// TANTIVY SCRAPER SETTINGS (VIKA / BORSEHUNG)
// =====================================================
builder.Services.Configure<TantivyScraperSettings>(
    builder.Configuration.GetSection(TantivyScraperSettings.SectionName));

builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.Sync.TantivyScrapeResultSyncService>();

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
builder.Services.AddScoped<SapItemUomWriter>();
builder.Services.AddScoped<SapItemSelector>();
builder.Services.AddScoped<SapLiquiMolyItemMapper>();
builder.Services.AddScoped<SapProductBarcodeReader>();
builder.Services.AddScoped<SapProductBarcodeWriter>();
builder.Services.AddScoped<SapItemBarcodeWriteService>();
builder.Services.AddScoped<SapLiquiMolyStockReader>();
builder.Services.AddScoped<SapGoodsIssueWriter>();
builder.Services.AddScoped<SapGoodsReceiptWriter>();
builder.Services.AddScoped<SapInventoryTransferRequestWriter>();
builder.Services.AddScoped<SapSalesOrderCreator>();
builder.Services.AddScoped<SapSalesOrderCanceler>();
builder.Services.AddScoped<SapSalesOrderLineDescriptionUpdater>();
builder.Services.AddScoped<SapQuotationConverter>();
builder.Services.AddScoped<SapOpenSalesOrderWarehouseUpdater>();
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
builder.Services.AddScoped<MolasLubes.Infrastructure.Services.Caching.NeonApiCacheService>();
builder.Services.AddScoped<MolasLubes.Infrastructure.Services.Caching.NeonInventoryService>();
builder.Services.AddScoped<MolasLubes.Infrastructure.Services.Caching.AutoHubNeonInventoryService>();
builder.Services.AddScoped<MolasLubes.Infrastructure.Services.Caching.AutoHubNeonDeliveryService>();

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
builder.Services.AddScoped<InventoryCountingUomBackfillService>();
builder.Services.AddScoped<BulkInventoryCountingUomBackfillService>();
builder.Services.AddSingleton<TransferRefGenerator>();
builder.Services.AddScoped<LiquiMolyTransferService>();

// =====================================================
// LIQUI-MOLY REPLENISHMENT — Phases 1–6
// =====================================================
builder.Services.Configure<MolasLubes.Infrastructure.Security.LiquiMolyPermissionsOptions>(
    builder.Configuration.GetSection(
        MolasLubes.Infrastructure.Security.LiquiMolyPermissionsOptions.SectionName));

builder.Services.Configure<ApnsOptions>(options =>
{
    builder.Configuration.GetSection(ApnsOptions.SectionName).Bind(options);
    options.KeyId = FirstNonEmpty(options.KeyId, builder.Configuration["APNS_KEY_ID"]);
    options.TeamId = FirstNonEmpty(options.TeamId, builder.Configuration["APNS_TEAM_ID"]);
    options.BundleId = FirstNonEmpty(options.BundleId, builder.Configuration["APNS_BUNDLE_ID"]);
    options.AuthKeyPath = FirstNonEmpty(options.AuthKeyPath, builder.Configuration["APNS_AUTH_KEY_PATH"]);
    options.Env = FirstNonEmpty(options.Env, builder.Configuration["APNS_ENV"], "production");
});

builder.Services.AddScoped<MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapUserReader>();
builder.Services.AddScoped<MolasLubes.Infrastructure.Security.LiquiMolyRoleService>();

builder.Services.AddScoped<MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapLiquiMolyDemandReader>();
builder.Services.AddScoped<MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapLiquiMolySourceMapReader>();
builder.Services.AddScoped<LiquiMolyReplenishmentAnalyzer>();

builder.Services.AddSingleton<ReplenishmentRefGenerator>();
builder.Services.AddScoped<LiquiMolyReplenishmentService>();
builder.Services.AddScoped<LiquiMolyReplenishmentExecutionService>();
builder.Services.AddScoped<LiquiMolyPushNotificationService>();
builder.Services.AddHttpClient<ApnsNotificationSender>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    EnableMultipleHttp2Connections = true,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    KeepAlivePingDelay = TimeSpan.FromMinutes(1),
    KeepAlivePingTimeout = TimeSpan.FromSeconds(30)
});

// Inter-company SO → PO → GR writers (Step 3, 4, 5)
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.LiquiMolyReplenishment.PL05PricingCalculator>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapInterCompanySalesOrderWriter>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapPurchaseOrderWriter>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapWarehouseReader>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapLiquiMolyDocumentReader>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapLiquiMolyInventoryReader>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapLiquiMolySalesOrderReportReader>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapLiquiMolyTransferReader>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.SapEventOutboxService>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.Sync.LiquiMolyTransferSyncService>();

// =====================================================
// AUTOHUB SERVICES — PROFILE B
// =====================================================
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapAutoHubSeedReader>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.Sync.GermaxCacheSyncService>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.Sync.GermaxAutoHubSyncService>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapAutoHubStockReader>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapAutoHubDocumentReader>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.Sync.AutoHubNeonStockSyncService>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.Sync.AutoHubNeonDocumentSyncService>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapAutoHubSalesPersonReader>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.Sync.AutoHubNeonSalesPersonSyncService>();
builder.Services.AddScoped<
    MolasLubes.Infrastructure.Services.Sync.TantivyPartsSyncService>();

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
builder.Services.AddSingleton<ManualProductScrapeQueueService>();

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
builder.Services.AddTransient<SapBarcodeFillJob>();
builder.Services.AddTransient<OpenSalesOrderWarehouseUpdateJob>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Scheduling.Jobs.AutoHubSapSeedSyncJob>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Scheduling.Jobs.NeonAutoHubStockSyncJob>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Scheduling.Jobs.AutoHubNeonDocumentSyncJob>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Scheduling.Jobs.GermaxProductEnrichmentJob>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Scheduling.Jobs.GermaxRetryFailedJob>();

builder.Services.AddTransient<
    MolasLubes.Infrastructure.Scheduling.Jobs.TantivyScraperJob>();
builder.Services.AddTransient<
    MolasLubes.Infrastructure.Scheduling.Jobs.LiquiMolyTransferSyncJob>();

// =====================================================
// QUARTZ CONFIGURATION
// =====================================================
builder.Services.AddQuartz(q =>
{
    q.UseDefaultThreadPool(tp =>
    {
        tp.MaxConcurrency = quartzMaxConcurrency;
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
    // Operational delta jobs run only during business hours: 06:00–20:00 EAT (UTC+3) = 03:00–16:59 UTC.
    // Full/catalog syncs keep their own off-hours schedules.
    // =========================
    RegisterJob<ProductFullSyncJob>("ProductFullSyncJob", "0 0 */6 ? * *"); // every 6 hours — catalog, runs any hour
    RegisterJob<CustomerDeltaSyncJob>("CustomerDeltaSyncJob", "0 */5 3-16 ? * *");         // every 5 min, 06:00–20:00 EAT
    RegisterJob<SalesOrderSyncJob>("SalesOrderSyncJob", "10 */5 3-16 ? * *");              // every 5 min, 06:00–20:00 EAT
    RegisterJob<MolasLubes.Infrastructure.Scheduling.Jobs.SapOpenOrdersSyncJob>("SapOpenOrdersSyncJob", "20 */5 3-16 ? * *"); // every 5 min, 06:00–20:00 EAT
    // RegisterJob<OpenSalesOrderWarehouseUpdateJob>("OpenSalesOrderWarehouseUpdateJob", "0 */5 3-16 ? * *"); // DISABLED
    RegisterJob<DeliveryDeltaSyncJob>("DeliveryDeltaSyncJob", "0/10 * 3-16 ? * *");        // every 10s, 06:00–20:00 EAT


    if (syncSettings.EnableInvoiceCacheSync)
        RegisterJob<InvoiceSyncJob>("InvoiceSyncJob", "3/10 * 3-16 ? * *");                // every 10s, 06:00–20:00 EAT

    if (syncSettings.EnablePaymentCacheSync)
        RegisterJob<PaymentSyncJob>("PaymentSyncJob", "6/10 * 3-16 ? * *");                // every 10s, 06:00–20:00 EAT

    // =========================
    // CACHE → NEON (second layer)
    // =========================
    if (syncSettings.EnableNeonCustomerSync)
        RegisterJob<NeonCustomerSyncJob>("NeonCustomerSyncJob", "5 */5 3-16 ? * *");       // every 5 min, 06:00–20:00 EAT

    if (syncSettings.EnableNeonInvoiceSync)
        RegisterJob<NeonInvoiceSyncJob>("NeonInvoiceSyncJob", "4/10 * 3-16 ? * *");        // every 10s, 06:00–20:00 EAT

    if (syncSettings.EnableNeonPaymentSync)
        RegisterJob<NeonPaymentSyncJob>("NeonPaymentSyncJob", "7/10 * 3-16 ? * *");        // every 10s, 06:00–20:00 EAT

    RegisterJob<NeonDeliverySyncJob>("NeonDeliverySyncJob", "1/10 * 3-16 ? * *");          // every 10s, 06:00–20:00 EAT

    // Odoo pushes: staggered to 15s intervals to reduce queue pressure
    if (syncSettings.EnableOdooDeliveryPush)
        RegisterJob<OdooDeliveryPushJob>("OdooDeliveryPushJob", "2/15 * 3-16 ? * *");      // every 15s, 06:00–20:00 EAT

    if (syncSettings.EnableOdooInvoicePush)
        RegisterJob<OdooInvoicePushJob>("OdooInvoicePushJob", "5/15 * 3-16 ? * *");        // every 15s, 06:00–20:00 EAT

    if (syncSettings.EnableOdooPaymentPush)
        RegisterJob<OdooPaymentPushJob>("OdooPaymentPushJob", "8/15 * 3-16 ? * *");        // every 15s, 06:00–20:00 EAT

    RegisterJob<NeonProductDeltaSyncJob>("NeonProductDeltaSyncJob", "55 */10 3-16 ? * *"); // every 10 min, 06:00–20:00 EAT
    RegisterJob<MolasLubes.Infrastructure.Scheduling.Jobs.NeonAutoHubStockSyncJob>(
        "NeonAutoHubStockSyncJob", "25 */10 3-16 ? * *");                                  // every 10 min, 06:00–20:00 EAT
    RegisterJob<MolasLubes.Infrastructure.Scheduling.Jobs.AutoHubNeonDocumentSyncJob>(
        "AutoHubNeonDocumentSyncJob", "0 */15 3-16 ? * *");                                // every 15 min, 06:00–20:00 EAT
    RegisterJob<NeonSalesOrderSyncJob>(
        "NeonSalesOrderSyncJob",
        "15 */5 3-16 ? * *");                                                               // every 5 min, 06:00–20:00 EAT

    RegisterJob<NeonSalesOrderLineSyncJob>(
        "NeonSalesOrderLineSyncJob",
        "25 */5 3-16 ? * *");                                                               // every 5 min, 06:00–20:00 EAT

    if (syncSettings.EnableLiquiMolyTransferSync)
    {
        // StoreDurably so POST /api/admin/sync/sap/liquimoly/transfers can fire it outside cron window
        q.AddJob<MolasLubes.Infrastructure.Scheduling.Jobs.LiquiMolyTransferSyncJob>(opts =>
            opts.WithIdentity("LiquiMolyTransferSyncJob").StoreDurably());

        q.AddTrigger(t => t
            .ForJob(new JobKey("LiquiMolyTransferSyncJob"))
            .WithIdentity("LiquiMolyTransferSyncJob-trigger")
            .WithCronSchedule("0 */2 3-16 ? * *"));                                        // every 2 min, 06:00–19:00 EAT
    }

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

    // Missing barcode auto-fill: daily at 03:30 UTC (= 06:30 EAT, after scrape finishes).
    // Scrapes EAN codes only for COCWHSE items still missing a unit barcode.
    // Auto-parts (BM/MB/VAG/VOL) and MANWHSE packaging gaps are skipped — use POST
    // /api/admin/items/{itemCode}/barcodes (mobile scan) for those.
    q.AddJob<SapBarcodeFillJob>(opts =>
        opts.WithIdentity("SapBarcodeFillJob")
            .StoreDurably());

    q.AddTrigger(t => t
        .ForJob(new JobKey("SapBarcodeFillJob"))
        .WithIdentity("SapBarcodeFillJob-trigger")
        .WithCronSchedule("0 30 3 ? * *")); // daily at 03:30 UTC = 06:30 EAT

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

    // Tantivy scraper (VIKA + Borsehung): nightly at 02:30 UTC
    // Also manually triggerable via POST /api/admin/autohub/tantivy/scrape-catalog
    q.AddJob<MolasLubes.Infrastructure.Scheduling.Jobs.TantivyScraperJob>(opts =>
        opts.WithIdentity("TantivyScraperJob")
            .StoreDurably());

    q.AddTrigger(t => t
        .ForJob(new JobKey("TantivyScraperJob"))
        .WithIdentity("TantivyScraperJob-trigger")
        .WithCronSchedule("0 30 2 ? * *")); // nightly at 02:30 UTC

    // Sales Order line description formatter: daily at 06:00 EAT (03:00 UTC).
    // Writes "U_ItemName/U_Manufacturer/Desc" prefix on every open order line
    // that is still missing it.  Idempotent — already-formatted lines are skipped.
    // Also manually triggerable via POST /api/admin/sync/sap/sales-orders/format-descriptions
    q.AddJob<MolasLubes.Infrastructure.Scheduling.Jobs.SalesOrderDescriptionFormatJob>(opts =>
        opts.WithIdentity("SalesOrderDescriptionFormatJob")
            .StoreDurably());

    q.AddTrigger(t => t
        .ForJob(new JobKey("SalesOrderDescriptionFormatJob"))
        .WithIdentity("SalesOrderDescriptionFormatJob-trigger")
        .WithCronSchedule("0 0 3 ? * *")); // daily at 06:00 EAT (03:00 UTC)
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
    var cfg = builder.Configuration;

    // Build the list of migrations to run (skip unconfigured ones).
    var migrations = new List<(string Label, Action Migrate)>();

    var sqlConn = cfg.GetConnectionString("MolasCacheDb");
    if (!string.IsNullOrWhiteSpace(sqlConn) && !sqlConn.StartsWith("CHANGE_ME"))
        migrations.Add(("MolasCacheDb",     () => scope.ServiceProvider.GetRequiredService<MolasCacheDbContext>().Database.Migrate()));
    else
        Log.Warning("MolasCacheDb connection string is not configured — skipping SQL Server migration.");

    var neonConn = cfg.GetConnectionString("NeonDb");
    if (!string.IsNullOrWhiteSpace(neonConn) && !neonConn.StartsWith("CHANGE_ME"))
        migrations.Add(("NeonDb",           () => scope.ServiceProvider.GetRequiredService<NeonDbContext>().Database.Migrate()));
    else
        Log.Warning("NeonDb connection string is not configured — skipping PostgreSQL migration.");

    var live2021CacheConn = cfg["IntegrationProfiles:Profiles:AutoHub:ConnectionStrings:CacheDb"];
    if (!string.IsNullOrWhiteSpace(live2021CacheConn) && !live2021CacheConn.StartsWith("CHANGE_ME"))
        migrations.Add(("Live2021CacheDb",  () => scope.ServiceProvider.GetRequiredService<Live2021CacheDbContext>().Database.Migrate()));
    else
        Log.Warning("AutoHub CacheDb connection string is not configured — skipping Live2021Cache migration.");

    var autoHubNeonConn = cfg["IntegrationProfiles:Profiles:AutoHub:ConnectionStrings:NeonDb"];
    if (!string.IsNullOrWhiteSpace(autoHubNeonConn) && !autoHubNeonConn.StartsWith("CHANGE_ME"))
        migrations.Add(("AutoHubNeonDb",    () => scope.ServiceProvider.GetRequiredService<AutoHubDbContext>().Database.Migrate()));
    else
        Log.Warning("AutoHub NeonDb connection string is not configured — skipping Parts_Catalog migration.");

    // Run all migrations in a background thread with a 20-second total budget so
    // the Windows Service startup timeout (30 s) is never hit. When DBs already
    // exist and are up-to-date this completes in < 1 s; the time cap only applies
    // when a DB server is unreachable (connection timeout), which is non-fatal.
    var migTask = Task.Run(() =>
    {
        foreach (var (label, migrate) in migrations)
        {
            try { migrate(); Log.Information("Migration applied: {Label}", label); }
            catch (Exception ex) { Log.Warning(ex, "Migration skipped — {Label}: {Message}", label, ex.Message); }
        }
    });

    if (!migTask.Wait(TimeSpan.FromSeconds(20)))
        Log.Warning("Migrations did not complete within 20 s — service will start anyway.");
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.EnablePersistAuthorization();
});
app.MapOpenApi();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Serve files from wwwroot (barcode-scanner.html, etc.)
app.UseStaticFiles();

// Authentication & Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

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

// ── --seed-admin CLI flag ────────────────────────────────────────────────
if (args.Contains("--seed-admin"))
{
    using var scope = app.Services.CreateScope();
    var userSvc = scope.ServiceProvider
        .GetRequiredService<MolasLubes.Infrastructure.Security.InternalUserService>();

    var (user, password) = await userSvc.EnsureAdminAsync();
    Console.WriteLine($"Admin user: {user.Username}");
    if (!string.IsNullOrEmpty(password))
        Console.WriteLine($"Generated password: {password}  (save this — it will not be shown again)");
    else
        Console.WriteLine("Admin already exists — password unchanged.");

    return;
}

// ── --unlock-admin CLI flag ──────────────────────────────────────────────
if (args.Contains("--unlock-admin"))
{
    using var scope = app.Services.CreateScope();
    var userSvc = scope.ServiceProvider
        .GetRequiredService<MolasLubes.Infrastructure.Security.InternalUserService>();

    var usernameArg = args.SkipWhile(a => a != "--unlock-admin").Skip(1).FirstOrDefault() ?? "admin";
    var unlocked = await userSvc.UnlockAsync(usernameArg);
    Console.WriteLine(unlocked
        ? $"Account '{usernameArg}' unlocked successfully."
        : $"User '{usernameArg}' not found.");

    return;
}

app.Run();

static string FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();
    }

    return string.Empty;
}
