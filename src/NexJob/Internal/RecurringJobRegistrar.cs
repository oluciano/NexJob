using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Storage;

namespace NexJob.Internal;

internal sealed class RecurringJobRegistrar
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IStorageProvider _storage;
    private readonly ILogger<RecurringJobRegistrar> _logger;
    private readonly List<string> _registeredJobIds = new();

    public RecurringJobRegistrar(
        IStorageProvider storage,
        ILogger<RecurringJobRegistrar> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public IReadOnlyList<string> RegisteredJobIds => _registeredJobIds;

    public async Task RegisterRecurringJobsAsync(
        IEnumerable<RecurringJobSettings> recurringJobs,
        CancellationToken cancellationToken = default)
    {
        foreach (var jobConfig in recurringJobs)
        {
            await RegisterRecurringJobAsync(jobConfig, cancellationToken);
        }

        _logger.LogInformation(
            "Completed registration of {Count} recurring jobs from configuration.",
            _registeredJobIds.Count);
    }

    private static void ValidateJobConfiguration(RecurringJobSettings jobConfig)
    {
        if (string.IsNullOrWhiteSpace(jobConfig.Id))
        {
            throw new ArgumentException("Recurring job ID is required.", nameof(jobConfig));
        }

        if (string.IsNullOrWhiteSpace(jobConfig.JobType))
        {
            throw new ArgumentException("Job type is required.", nameof(jobConfig));
        }

        if (string.IsNullOrWhiteSpace(jobConfig.Cron))
        {
            throw new ArgumentException("Cron expression is required.", nameof(jobConfig));
        }
    }

    private static bool IsValidJobType(Type type, Type? inputType = null)
    {
        if (inputType == null)
        {
            return Array.Exists(type.GetInterfaces(), i => i == typeof(IJob));
        }
        else
        {
            var jobInterface = typeof(IJob<>).MakeGenericType(inputType);
            return Array.Exists(type.GetInterfaces(), i => i == jobInterface);
        }
    }

    private static bool HasGenericJobInterface(Type type)
    {
        var genericInterface = typeof(IJob<>);
        return Array.Exists(type.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterface);
    }

    private static void ParseAndValidateCron(string cronExpression)
    {
        DefaultScheduler.ParseCron(cronExpression);
    }

    private static DateTimeOffset? CalculateNextExecution(RecurringJobSettings jobConfig)
    {
        try
        {
            // Validate cron expression can be parsed (should always succeed — already validated)
            _ = DefaultScheduler.ParseCron(jobConfig.Cron);

            // Set NextExecution to just before now so the job is immediately due for enqueue.
            // The scheduler will then enqueue and advance NextExecution to the actual next occurrence.
            return DateTimeOffset.UtcNow.AddSeconds(-1);
        }
        catch
        {
            // If cron parsing fails (shouldn't happen — already validated), return null
            // The scheduler will calculate the next execution on its first run
            return null;
        }
    }

    private async Task RegisterRecurringJobAsync(
        RecurringJobSettings jobConfig,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateJobConfiguration(jobConfig);

            var resolvedTypes = ResolveJobTypes(jobConfig);
            var jobType = resolvedTypes.JobType;
            var inputType = resolvedTypes.InputType;

            ParseAndValidateCron(jobConfig.Cron);

            var inputJson = jobConfig.InputJson != null
                ? SerializeInputJson(jobConfig.InputJson, inputType)
                : null;

            var nextExecution = CalculateNextExecution(jobConfig);

            var recurringJob = new RecurringJobRecord
            {
                RecurringJobId = jobConfig.Id,
                JobType = jobType.AssemblyQualifiedName!,
                InputType = inputType?.AssemblyQualifiedName ?? string.Empty,
                InputJson = inputJson ?? string.Empty,
                Cron = jobConfig.Cron,
                TimeZoneId = jobConfig.TimeZoneId,
                Queue = jobConfig.Queue,
                ConcurrencyPolicy = jobConfig.ConcurrencyPolicy,
                Enabled = jobConfig.Enabled,
                CreatedAt = DateTimeOffset.UtcNow,
                NextExecution = nextExecution,
            };

            var existingJob = await _storage.GetRecurringJobByIdAsync(jobConfig.Id, cancellationToken);
            if (existingJob != null)
            {
                _logger.LogWarning(
                    "Recurring job '{Id}' already exists. Skipping registration.",
                    jobConfig.Id);
                return;
            }

            await _storage.UpsertRecurringJobAsync(recurringJob, cancellationToken);

            _registeredJobIds.Add(jobConfig.Id);
            _logger.LogInformation(
                "Successfully registered recurring job '{Id}' with cron '{Cron}' in queue '{Queue}'",
                jobConfig.Id,
                jobConfig.Cron,
                jobConfig.Queue);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to register recurring job '{Id}' from configuration",
                jobConfig.Id);
        }
    }

    private (Type JobType, Type? InputType) ResolveJobTypes(RecurringJobSettings jobConfig)
    {
        try
        {
            var jobType = Type.GetType(jobConfig.JobType);
            if (jobType == null)
            {
                throw new TypeLoadException($"Could not find job type: {jobConfig.JobType}");
            }

            if (!IsValidJobType(jobType))
            {
                throw new InvalidOperationException(
                    $"Job type {jobConfig.JobType} must implement IJob or IJob<T>.");
            }

            Type? inputType = null;
            if (!string.IsNullOrWhiteSpace(jobConfig.InputType))
            {
                inputType = Type.GetType(jobConfig.InputType);
                if (inputType == null)
                {
                    throw new TypeLoadException($"Could not find input type: {jobConfig.InputType}");
                }

                if (!IsValidJobType(jobType, inputType))
                {
                    throw new InvalidOperationException(
                        $"Job type {jobConfig.JobType} does not implement IJob<{inputType.Name}>.");
                }
            }
            else if (HasGenericJobInterface(jobType))
            {
                throw new InvalidOperationException(
                    $"Job type {jobConfig.JobType} implements IJob<T> but no input type was specified.");
            }

            return (jobType, InputType: inputType);
        }
        catch (TypeLoadException ex)
        {
            _logger.LogError(
                ex,
                "Type resolution failed for recurring job '{Id}': {JobType}",
                jobConfig.Id,
                jobConfig.JobType);
            throw new TypeLoadException($"Could not resolve job types for recurring job '{jobConfig.Id}'", ex);
        }
    }

    private string? SerializeInputJson(string inputJson, Type? inputType)
    {
        if (inputType == null || string.IsNullOrWhiteSpace(inputJson))
        {
            return null;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize(inputJson, inputType, JsonOptions);
            if (deserialized == null)
            {
                throw new JsonException($"Input JSON deserialized to null for type: {inputType}");
            }

            return inputJson;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize input JSON for type: {InputType}", inputType);
            throw new JsonException($"Failed to validate input JSON for type '{inputType.Name}'", ex);
        }
    }
}
