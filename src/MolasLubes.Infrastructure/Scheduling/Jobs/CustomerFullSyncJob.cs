using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Caching;
using Quartz;
using System.Diagnostics;

namespace MolasLubes.Infrastructure.Scheduling.Jobs
{
    [DisallowConcurrentExecution]
    public class CustomerFullSyncJob : IJob
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CustomerFullSyncJob> _logger;

        public CustomerFullSyncJob(
            IServiceScopeFactory scopeFactory,
            ILogger<CustomerFullSyncJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogWarning("🔥 CUSTOMER FULL SYNC STARTED");

            using var scope = _scopeFactory.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<SapCustomerReader>();
            var cache = scope.ServiceProvider.GetRequiredService<CustomerCacheService>();

            try
            {
                // ✅ FAST CLEAR (see method below)
                await cache.ClearAllCustomersFastAsync();

                const int batchSize = 500;
                var lastCardCode = "";
                var total = 0;
                var batchNo = 0;

                while (true)
                {
                    batchNo++;

                    _logger.LogInformation(
                        "📡 Reading SAP batch #{BatchNo} | After={LastCardCode} | Size={BatchSize}",
                        batchNo, lastCardCode, batchSize);

                    var batch = reader.ReadCustomerBatchAfter(lastCardCode, batchSize);

                    _logger.LogInformation(
                        "📦 Batch #{BatchNo} loaded | Count={Count}",
                        batchNo, batch.Count);

                    if (batch.Count == 0)
                    {
                        _logger.LogWarning("✅ No more customers in SAP. Full sync done.");
                        break;
                    }

                    await cache.UpsertCustomersAsync(batch);

                    total += batch.Count;
                    lastCardCode = batch[^1].CardCode; // last row in batch

                    _logger.LogInformation(
                        "✅ Batch #{BatchNo} saved to CacheDb | TotalSaved={Total} | LastCardCode={LastCardCode}",
                        batchNo, total, lastCardCode);

                    // stop if last batch smaller than batch size
                    if (batch.Count < batchSize)
                        break;
                }

                sw.Stop();
                _logger.LogWarning(
                    "🏁 CUSTOMER FULL SYNC COMPLETED | Total={Total} | DurationSec={Sec}",
                    total, sw.Elapsed.TotalSeconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "❌ CUSTOMER FULL SYNC FAILED | DurationSec={Sec}",
                    sw.Elapsed.TotalSeconds);
                throw;
            }
        }
    }
}