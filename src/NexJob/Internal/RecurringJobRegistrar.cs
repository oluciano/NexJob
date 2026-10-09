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
    private readonly NexJobOptions? _options;
    private readonly List<string> _registeredJobIds = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringJobRegistrar"/> class.
    /// </summary>
    /// <param name="storage">The storage provider.</param>
    /// <param name="jobRegistry">The job registry.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="options">NexJob options, used to resolve the stored queue name.</param>
    public RecurringJobRegistrar(
        IRecurringStorage storage,
        NexJobJobRegistry jobRegistry,
        ILogger<RecurringJobRegistrar> logger,
        NexJobOptions? options = null)
    {
        _options = options;
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

    /// <summary>
    /// Returns a type name without its assembly part: the text up to the first comma that is not inside brackets.
    /// The assembly part carries the version, which changes on every deploy.
    /// </summary>
    /// <param name="assemblyQualifiedName">An assembly-qualified type name.</param>
    /// <returns>The type name without the assembly.</returns>
    internal static string TypeNameWithoutAssembly(string assemblyQualifiedName) => assemblyQualifiedName;

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

            // Resolve the time zone here, before anything is written: an invalid TimeZoneId fails this registration
            // (logged below) instead of storing a job that the scheduler could never compute a next run for.
            var timeZone = jobConfig.TimeZoneId is not null
                ? TimeZoneInfo.FindSystemTimeZoneById(jobConfig.TimeZoneId)
                : TimeZoneInfo.Utc;

            // The first run happens at the next cron occurrence, like a job registered from code. A timestamp in the
            // past would make every job fire immediately on each (re)start.
            var nextExecution = DefaultScheduler.ParseCron(jobConfig.Cron).GetNextOccurrence(DateTimeOffset.UtcNow, timeZone);

            var recurringJob = new RecurringJobRecord
            {
                RecurringJobId = effectiveId,
                JobType = jobType.AssemblyQualifiedName!,
                InputType = inputType?.AssemblyQualifiedName ?? typeof(NoInput).AssemblyQualifiedName!,
                InputJson = inputJson,
                Cron = jobConfig.Cron,
                TimeZoneId = jobConfig.TimeZoneId,
                Queue = _options?.ResolveQueue(jobConfig.Queue) ?? jobConfig.Queue,
                ConcurrencyPolicy = jobConfig.ConcurrencyPolicy,
                Enabled = jobConfig.Enabled,
                CreatedAt = DateTimeOffset.UtcNow,
                NextExecution = nextExecution,
            };

            // Always upsert: the definition in configuration wins on every start, while the fields an operator changes
            // in the dashboard (cron override, paused, deleted) are preserved by the storage on conflict.
            await _storage.UpsertRecurringJobAsync(recurringJob, cancellationToken).ConfigureAwait(false);

            _registeredJobIds.Add(effectiveId);
            _logger.LogInformation(
                "Registered or updated recurring job '{Id}' with cron '{Cron}' in queue '{Queue}'",
                effectiveId,
                jobConfig.Cron,
                recurringJob.Queue);
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
