using Microsoft.EntityFrameworkCore;
using MolasLubes.Api.Security;
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
using Quartz;

var builder = WebApplication.CreateBuilder(args);

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
// DATABASES
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
// SAP SETTINGS
// =====================================================
builder.Services.Configure<SapSettings>(
    builder.Configuration.GetSection("SAP"));

// =====================================================
// SAP DI SERVICES
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
// ODOO PUSH SERVICES
// =====================================================
builder.Services.AddHttpClient<OdooApiClient>(client =>
{
    var baseUrl = builder.Configuration["OdooApi:BaseUrl"] ?? "";
    if (!string.IsNullOrWhiteSpace(baseUrl))
        client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddScoped<OdooDeliveryPushService>();

// =====================================================
// QUARTZ JOB REGISTRATION
// =====================================================
builder.Services.AddTransient<ProductFullSyncJob>();
builder.Services.AddTransient<CustomerDeltaSyncJob>();
builder.Services.AddTransient<CustomerFullSyncJob>();
builder.Services.AddTransient<SalesOrderSyncJob>();
builder.Services.AddTransient<InvoiceSyncJob>();
builder.Services.AddTransient<InvoiceFullSyncJob>();
builder.Services.AddTransient<PaymentSyncJob>();
builder.Services.AddTransient<DeliveryDeltaSyncJob>();
builder.Services.AddTransient<OdooDeliveryPushJob>();

builder.Services.AddTransient<NeonCustomerSyncJob>();
builder.Services.AddTransient<NeonProductDeltaSyncJob>();
builder.Services.AddTransient<NeonInvoiceSyncJob>();
builder.Services.AddTransient<NeonPaymentSyncJob>();
builder.Services.AddTransient<NeonDeliverySyncJob>();
builder.Services.AddTransient<NeonSalesOrderSyncJob>();
builder.Services.AddTransient<NeonSalesOrderLineSyncJob>();
builder.Services.AddTransient<NeonPriceListSyncJob>();

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
    RegisterJob<DeliveryDeltaSyncJob>("DeliveryDeltaSyncJob", "20 */5 * ? * *");


    if (syncSettings.EnableInvoiceCacheSync)
        RegisterJob<InvoiceSyncJob>("InvoiceSyncJob", "30 */5 * ? * *");

    if (syncSettings.EnablePaymentCacheSync)
        RegisterJob<PaymentSyncJob>("PaymentSyncJob", "40 */5 * ? * *");

    // =========================
    // CACHE → NEON (second layer)
    // =========================
    if (syncSettings.EnableNeonCustomerSync)
        RegisterJob<NeonCustomerSyncJob>("NeonCustomerSyncJob", "5 */5 * ? * *");

    if (syncSettings.EnableNeonInvoiceSync)
        RegisterJob<NeonInvoiceSyncJob>("NeonInvoiceSyncJob", "35 */5 * ? * *");

    if (syncSettings.EnableNeonPaymentSync)
        RegisterJob<NeonPaymentSyncJob>("NeonPaymentSyncJob", "45 */5 * ? * *");

    RegisterJob<NeonDeliverySyncJob>("NeonDeliverySyncJob", "50 */5 * ? * *");

    if (syncSettings.EnableOdooDeliveryPush)
        RegisterJob<OdooDeliveryPushJob>("OdooDeliveryPushJob", "57 */5 * ? * *");

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
    scope.ServiceProvider
        .GetRequiredService<MolasCacheDbContext>()
        .Database.Migrate();

    scope.ServiceProvider
        .GetRequiredService<NeonDbContext>()
        .Database.Migrate();
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