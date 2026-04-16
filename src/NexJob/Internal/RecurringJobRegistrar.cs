using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Orchestrates the registration and update of recurring jobs from configuration.
/// </summary>
internal sealed class RecurringJobRegistrar
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IRecurringStorage _storage;
    private readonly NexJobJobRegistry _jobRegistry;
    private readonly ILogger<RecurringJobRegistrar> _logger;
    private readonly List<string> _registeredJobIds = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringJobRegistrar"/> class.
    /// </summary>
    /// <param name="storage">The storage provider.</param>
    /// <param name="jobRegistry">The job registry.</param>
    /// <param name="logger">The logger.</param>
    public RecurringJobRegistrar(
        IRecurringStorage storage,
        NexJobJobRegistry jobRegistry,
        ILogger<RecurringJobRegistrar> logger)
    {
        _storage = storage;
        _jobRegistry = jobRegistry;
        _logger = logger;
    }

    /// <summary>Gets the list of IDs that were successfully registered during the current session.</summary>
    public IReadOnlyList<string> RegisteredJobIds => _registeredJobIds;

    /// <summary>
    /// Registers a collection of recurring jobs.
    /// </summary>
    /// <param name="recurringJobs">The settings for the jobs to register.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RegisterRecurringJobsAsync(
        IEnumerable<RecurringJobSettings> recurringJobs,
        CancellationToken cancellationToken = default)
    {
        var configs = recurringJobs.ToList();
        var assignments = AssignEffectiveIds(configs);

        foreach (var (config, effectiveId) in assignments)
        {
            await RegisterRecurringJobAsync(config, effectiveId, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Completed registration of {Count} recurring jobs from configuration.",
            _registeredJobIds.Count);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Static Helpers
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the configuration for a recurring job.
    /// </summary>
    /// <param name="jobConfig">The job configuration to validate.</param>
    private static void ValidateJobConfiguration(RecurringJobSettings jobConfig)
    {
        if (string.IsNullOrWhiteSpace(jobConfig.Job))
        {
            throw new ArgumentException("Job name is required.", nameof(jobConfig));
        }

        if (string.IsNullOrWhiteSpace(jobConfig.Cron))
        {
            throw new ArgumentException("Cron expression is required.", nameof(jobConfig));
        }
    }

    /// <summary>
    /// Parses and validates a cron expression.
    /// </summary>
    /// <param name="cronExpression">The cron expression to validate.</param>
    private static void ParseAndValidateCron(string cronExpression)
    {
        DefaultScheduler.ParseCron(cronExpression);
    }

    /// <summary>
    /// Calculates the next execution time for a job.
    /// </summary>
    /// <param name="jobConfig">The job configuration.</param>
    /// <returns>The calculated next execution time, or null if invalid.</returns>
    private static DateTimeOffset? CalculateNextExecution(RecurringJobSettings jobConfig)
    {
        try
        {
            _ = DefaultScheduler.ParseCron(jobConfig.Cron);
            return DateTimeOffset.UtcNow.AddSeconds(-1);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the input type for a given job type.
    /// </summary>
    /// <param name="jobType">The job type.</param>
    /// <returns>The resolved input type, or null if none.</returns>
    private static Type? ResolveInputType(Type jobType)
    {
        var jobInterface = Array.Find(
            jobType.GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJob<>));
        return jobInterface?.GetGenericArguments()[0];
    }

    /// <summary>
    /// Serializes input for a job based on its type.
    /// </summary>
    /// <param name="inputJson">The raw input JSON.</param>
    /// <param name="inputType">The resolved input type.</param>
    /// <returns>The serialized input JSON.</returns>
    private static string SerializeInput(string? inputJson, Type? inputType)
    {
        if (inputType == null || string.IsNullOrWhiteSpace(inputJson))
        {
            return JsonSerializer.Serialize(NoInput.Instance);
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize(inputJson, inputType, JsonOptions)
                ?? throw new InvalidOperationException(
                    $"Input JSON could not be deserialized to {inputType.Name}.");
            return JsonSerializer.Serialize(deserialized, JsonOptions);
        }
        catch (Exception ex)
        {
            throw new JsonException(
                $"Failed to validate input JSON for type '{inputType.Name}'", ex);
        }
    }

    /// <summary>
    /// Assigns effective IDs to job configurations, handling duplicates and defaults.
    /// </summary>
    /// <param name="configs">The job configurations.</param>
    /// <returns>A collection of configurations with their assigned effective IDs.</returns>
    private static IEnumerable<(RecurringJobSettings Config, string EffectiveId)> AssignEffectiveIds(
        IEnumerable<RecurringJobSettings> configs)
    {
        var list = configs.ToList();
        var nameCount = new Dictionary<string, int>(StringComparer.Ordinal);

        // Count how many times each unnamed job appears
        foreach (var name in list
            .Where(c => string.IsNullOrWhiteSpace(c.Id))
            .Select(c => c.Job))
        {
            nameCount[name] = nameCount.GetValueOrDefault(name) + 1;
        }

        var nameIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in list)
        {
            string id;
            if (!string.IsNullOrWhiteSpace(c.Id))
            {
                id = c.Id;
            }
            else if (nameCount[c.Job] == 1)
            {
                id = c.Job;
            }
            else
            {
                var idx = nameIndex.GetValueOrDefault(c.Job, 0);
                if (idx == 0)
                {
                    // First occurrence with duplicate names gets no suffix
                    id = c.Job;
                }
                else
                {
                    // Subsequent occurrences get suffix starting from -1
                    id = $"{c.Job}-{idx}";
                }

                nameIndex[c.Job] = idx + 1;
            }

            yield return (c, id);
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Instance Methods
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a single recurring job.
    /// </summary>
    /// <param name="jobConfig">The job configuration.</param>
    /// <param name="effectiveId">The effective ID assigned to this job.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RegisterRecurringJobAsync(
        RecurringJobSettings jobConfig,
        string effectiveId,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateJobConfiguration(jobConfig);

            var jobType = jobConfig.ResolvedJobType ?? ResolveJobTypeByName(jobConfig.Job);
            var inputType = ResolveInputType(jobType);

            ParseAndValidateCron(jobConfig.Cron);

            var inputJson = jobConfig.ResolvedInputJson ?? SerializeInput(jobConfig.Input, inputType);

            var nextExecution = CalculateNextExecution(jobConfig);

            var recurringJob = new RecurringJobRecord
            {
                RecurringJobId = effectiveId,
                JobType = jobType.AssemblyQualifiedName!,
                InputType = inputType?.AssemblyQualifiedName ?? typeof(NoInput).AssemblyQualifiedName!,
                InputJson = inputJson,
                Cron = jobConfig.Cron,
                TimeZoneId = jobConfig.TimeZoneId,
                Queue = jobConfig.Queue,
                ConcurrencyPolicy = jobConfig.ConcurrencyPolicy,
                Enabled = jobConfig.Enabled,
                CreatedAt = DateTimeOffset.UtcNow,
                NextExecution = nextExecution,
            };

            var existingJob = await _storage.GetRecurringJobByIdAsync(effectiveId, cancellationToken).ConfigureAwait(false);
            if (existingJob != null)
            {
                _logger.LogWarning(
                    "Recurring job '{Id}' already exists. Skipping registration.",
                    effectiveId);
                return;
            }

            await _storage.UpsertRecurringJobAsync(recurringJob, cancellationToken).ConfigureAwait(false);

            _registeredJobIds.Add(effectiveId);
            _logger.LogInformation(
                "Successfully registered recurring job '{Id}' with cron '{Cron}' in queue '{Queue}'",
                effectiveId,
                jobConfig.Cron,
                jobConfig.Queue);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to register recurring job '{Job}' from configuration",
                jobConfig.Job);
        }
    }

    /// <summary>
    /// Resolves a job type by its name using the job registry.
    /// </summary>
    /// <param name="jobName">The name of the job to resolve.</param>
    /// <returns>The resolved job type.</returns>
    private Type ResolveJobTypeByName(string jobName)
    {
        var matches = _jobRegistry.Types
            .Where(t => string.Equals(t.Name, jobName, StringComparison.Ordinal))
            .ToList();

        return matches.Count switch
        {
            0 => throw new InvalidOperationException(
                $"No job named '{jobName}' found. " +
                $"Ensure it is registered via AddNexJobJobs() or AddTransient<{jobName}>()."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Multiple jobs named '{jobName}' found:\n" +
                string.Join("\n", matches.Select(t => $"  {t.FullName}")) +
                $"\nUse explicit Id or rename one of the jobs."),
        };
    }
}
