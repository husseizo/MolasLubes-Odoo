using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Services.Caching;
using MolasLubes.Infrastructure.Services.Sync;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
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
// INVOICE SERVICES (OINV + INV1)
// =====================================================
builder.Services.AddScoped<InvoiceCacheService>();
builder.Services.AddScoped<NeonInvoiceSyncService>();
builder.Services.AddScoped<SalesOrderStatusService>(); // required by InvoiceCacheService

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// =====================================================
// DEBUG
// =====================================================
app.MapGet("/debug/cache-counts", async (MolasCacheDbContext db) =>
{
    return new
    {
        Invoices = await db.CacheInvoices.CountAsync(),
        InvoiceLines = await db.CacheInvoiceLines.CountAsync()
    };
});

app.MapGet("/", () => Results.Ok("MolasLubesOdoo API is running 🚀"));

app.Run();
