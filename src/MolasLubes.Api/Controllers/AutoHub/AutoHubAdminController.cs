using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

namespace MolasLubes.Api.Controllers.AutoHub;

[ApiController]
[Route("api/admin/autohub")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AutoHubAdminController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly GermaxAutoHubSyncService _germaxSyncService;

    public AutoHubAdminController(
        ISchedulerFactory schedulerFactory,
        GermaxAutoHubSyncService germaxSyncService)
    {
        _schedulerFactory = schedulerFactory;
        _germaxSyncService = germaxSyncService;
    }

    // -------------------------------------------------
    // SEED SYNC — trigger SAP → cache seed immediately
    // -------------------------------------------------
    [HttpPost("seed-sync")]
    public async Task<IActionResult> RunSeedSync()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("AutoHubSapSeedSyncJob"));

        return Ok(new { Message = "AutoHub SAP seed sync triggered successfully" });
    }

    // -------------------------------------------------
    // GERMAX SCRAPE — trigger enrichment job immediately
    // -------------------------------------------------
    [HttpPost("germax/scrape")]
    public async Task<IActionResult> RunGermaxScrape()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("GermaxProductEnrichmentJob"));

        return Ok(new { Message = "Germax enrichment job triggered successfully" });
    }

    // -------------------------------------------------
    // GERMAX RETRY — trigger retry of recent failures
    // -------------------------------------------------
    [HttpPost("germax/retry-failed")]
    public async Task<IActionResult> RunGermaxRetryFailed()
    {
        var scheduler = await _schedulerFactory.GetScheduler();

        // bypassAgeFilter=true so manual triggers can retry stale failures
        // that fall outside the 7-day window the scheduled job enforces.
        var jobData = new JobDataMap { ["bypassAgeFilter"] = true };
        await scheduler.TriggerJob(new JobKey("GermaxRetryFailedJob"), jobData);

        return Ok(new { Message = "Germax retry-failed job triggered successfully (all-time window)" });
    }

    // -------------------------------------------------
    // GERMAX SYNC — push SCRAPED cache rows to Parts_Catalog
    // -------------------------------------------------
    [HttpPost("germax/sync-neon")]
    public async Task<IActionResult> RunGermaxSyncNeon(CancellationToken ct)
    {
        await _germaxSyncService.SyncAsync(ct);

        return Ok(new
        {
            Message = "Germax cache sync to Parts_Catalog completed successfully"
        });
    }
}
