namespace NexJob.Internal;

/// <summary>
/// Builds and executes the <see cref="IJobExecutionFilter"/> pipeline for a single job execution.
/// Filters are chained in registration order; the job invocation is the terminal step.
/// </summary>
internal static class JobFilterPipeline
{
    /// <summary>
    /// Builds a pipeline from the provided filters and a terminal job invoker.
    /// Returns a single delegate that, when called, runs all filters in order
    /// and then invokes <paramref name="jobInvoker"/>.
    /// </summary>
    internal static JobExecutionDelegate Build(
        IReadOnlyList<IJobExecutionFilter> filters,
        JobExecutingContext context,
        JobExecutionDelegate jobInvoker)
    {
        // Build the chain from the end backwards — last filter wraps the job
        JobExecutionDelegate pipeline = jobInvoker;

        for (var i = filters.Count - 1; i >= 0; i--)
        {
            var filter = filters[i];
            var next = pipeline;

            pipeline = async ct =>
            {
                await filter.OnExecutingAsync(context, RecordOutcome(context, next), ct).ConfigureAwait(false);
            };
        }

        return pipeline;
    }

    // The filter reads the outcome right after `await next(ct)` or in its catch, so it is recorded here, not after the pipeline.
    private static JobExecutionDelegate RecordOutcome(JobExecutingContext context, JobExecutionDelegate next) =>
        async ct =>
        {
            try
            {
                await next(ct).ConfigureAwait(false);
                context.Succeeded = true;
            }
            catch (Exception ex)
            {
                context.Exception = ex;
                context.Succeeded = false;
                throw;
            }
        };
}
