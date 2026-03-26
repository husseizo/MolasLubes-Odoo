using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using Quartz;

namespace MolasLubes.Api.Controllers.AutoHub;

[ApiController]
[Route("api/admin/autohub")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AutoHubAdminController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;

    public AutoHubAdminController(ISchedulerFactory schedulerFactory)
    {
        _schedulerFactory = schedulerFactory;
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
        await scheduler.TriggerJob(new JobKey("GermaxRetryFailedJob"));

        return Ok(new { Message = "Germax retry-failed job triggered successfully" });
    }
}
