using Quartz;

namespace MolasLubes.Infrastructure.Scheduling;

public static class QuartzRetryHelper
{
    private const int MaxRetries = 3;
    private const int BaseDelaySeconds = 5;

    public static async Task HandleRetryAsync(
        IJobExecutionContext context,
        Exception ex)
    {
        if (context.RefireCount >= MaxRetries)
            throw ex;

        var exponentialDelay =
            Math.Pow(2, context.RefireCount) * BaseDelaySeconds;

        var jitter = Random.Shared.Next(0, 3);
        var delaySeconds = exponentialDelay + jitter;

        var newTrigger = TriggerBuilder.Create()
            .ForJob(context.JobDetail)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(delaySeconds))
            .Build();

        await context.Scheduler.ScheduleJob(newTrigger);

        throw new JobExecutionException(ex, false);
    }
}