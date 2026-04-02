using NexJob.Configuration;

namespace NexJob;

/// <summary>
/// Extension methods for recurring job registration via fluent API.
/// </summary>
public static class RecurringJobServiceCollectionExtensions
{
    /// <summary>
    /// Adds a recurring job with strongly-typed job class (no input) to the configuration.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob"/>.</typeparam>
    /// <param name="options">The NexJob options.</param>
    /// <param name="id">Unique identifier for the recurring job.</param>
    /// <param name="cron">Cron expression that defines the execution schedule.</param>
    /// <param name="queue">Name of the queue where the recurring job's instances will be enqueued.</param>
    /// <param name="timeZoneId">IANA or Windows time-zone ID used when evaluating the cron expression.</param>
    /// <param name="concurrencyPolicy">Controls what happens when a new firing occurs while a previous instance is still running.</param>
    /// <param name="enabled">When false, the scheduler skips this job at every firing.</param>
    /// <returns>The original options for chaining.</returns>
    public static NexJobOptions AddRecurringJob<TJob>(
        this NexJobOptions options,
        string id,
        string cron,
        string queue = "default",
        string? timeZoneId = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        bool enabled = true)
        where TJob : IJob
    {
        options.RecurringJobs.Add(new RecurringJobSettings
        {
            Job = typeof(TJob).Name,
            ResolvedJobType = typeof(TJob),
            Id = id,
            Cron = cron,
            Queue = queue,
            TimeZoneId = timeZoneId,
            ConcurrencyPolicy = concurrencyPolicy,
            Enabled = enabled,
        });

        return options;
    }

    /// <summary>
    /// Adds a recurring job with input to the configuration with strongly-typed job and input types.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/>.</typeparam>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <param name="options">The NexJob options.</param>
    /// <param name="id">Unique identifier for the recurring job.</param>
    /// <param name="cron">Cron expression that defines the execution schedule.</param>
    /// <param name="inputJson">JSON-serialized job input payload.</param>
    /// <param name="queue">Name of the queue where the recurring job's instances will be enqueued.</param>
    /// <param name="timeZoneId">IANA or Windows time-zone ID used when evaluating the cron expression.</param>
    /// <param name="concurrencyPolicy">Controls what happens when a new firing occurs while a previous instance is still running.</param>
    /// <param name="enabled">When false, the scheduler skips this job at every firing.</param>
    /// <returns>The original options for chaining.</returns>
    public static NexJobOptions AddRecurringJob<TJob, TInput>(
        this NexJobOptions options,
        string id,
        string cron,
        string inputJson,
        string queue = "default",
        string? timeZoneId = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        bool enabled = true)
        where TJob : IJob<TInput>
    {
        options.RecurringJobs.Add(new RecurringJobSettings
        {
            Job = typeof(TJob).Name,
            ResolvedJobType = typeof(TJob),
            ResolvedInputJson = inputJson,
            Id = id,
            Cron = cron,
            Queue = queue,
            TimeZoneId = timeZoneId,
            ConcurrencyPolicy = concurrencyPolicy,
            Enabled = enabled,
        });

        return options;
    }

    /// <summary>
    /// Adds a recurring job with input to the configuration with strongly-typed job type and input object.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/>.</typeparam>
    /// <typeparam name="TInput">The input type.</typeparam>
    /// <param name="options">The NexJob options.</param>
    /// <param name="id">Unique identifier for the recurring job.</param>
    /// <param name="cron">Cron expression that defines the execution schedule.</param>
    /// <param name="input">The input object to serialize as JSON.</param>
    /// <param name="queue">Name of the queue where the recurring job's instances will be enqueued.</param>
    /// <param name="timeZoneId">IANA or Windows time-zone ID used when evaluating the cron expression.</param>
    /// <param name="concurrencyPolicy">Controls what happens when a new firing occurs while a previous instance is still running.</param>
    /// <param name="enabled">When false, the scheduler skips this job at every firing.</param>
    /// <returns>The original options for chaining.</returns>
    public static NexJobOptions AddRecurringJob<TJob, TInput>(
        this NexJobOptions options,
        string id,
        string cron,
        TInput input,
        string queue = "default",
        string? timeZoneId = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        bool enabled = true)
        where TJob : IJob<TInput>
    {
        var inputJson = System.Text.Json.JsonSerializer.Serialize(input);
        return AddRecurringJob<TJob, TInput>(
            options,
            id,
            cron,
            inputJson,
            queue,
            timeZoneId,
            concurrencyPolicy,
            enabled);
    }
}
