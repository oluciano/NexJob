using NexJob;

namespace NexJob.Sample.WebApi.Jobs;

public sealed record ReportRequest(string ReportName, DateOnly From, DateOnly To);

public sealed class GenerateReportJob(ILogger<GenerateReportJob> logger) : IJob<ReportRequest>
{
    public async Task ExecuteAsync(ReportRequest input, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[GenerateReportJob] Generating '{ReportName}' for {From} → {To}",
            input.ReportName, input.From, input.To);

        await Task.Delay(1_000, cancellationToken); // simulate heavy query

        logger.LogInformation("[GenerateReportJob] '{ReportName}' ready.", input.ReportName);
    }
}
