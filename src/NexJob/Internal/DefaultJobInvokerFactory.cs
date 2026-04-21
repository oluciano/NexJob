using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Default implementation that prepares job instances and compiled invokers for execution.
/// </summary>
internal sealed class DefaultJobInvokerFactory : IJobInvokerFactory
{
    private static readonly ConcurrentDictionary<(Type Job, Type Input), Func<object, object, CancellationToken, Task>>
        InvokerCache = new();

    private readonly IJobStorage _storage;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultJobInvokerFactory"/> class.
    /// </summary>
    /// <param name="storage">The job storage used by the job context.</param>
    /// <param name="scopeFactory">The scope factory used to create execution scopes.</param>
    public DefaultJobInvokerFactory(IJobStorage storage, IServiceScopeFactory scopeFactory)
    {
        _storage = storage;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc/>
    public Task<JobInvocationContext> PrepareAsync(JobRecord job, CancellationToken ct = default)
    {
        var scope = _scopeFactory.CreateScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<IJobContextAccessor>().Context =
                new JobContext(job, _storage);

            var jobType = JobTypeResolver.ResolveJobType(job.JobType)
                          ?? throw new InvalidOperationException($"Cannot load job type: {job.JobType}");
            var inputType = JobTypeResolver.ResolveInputType(job.InputType)
                           ?? throw new InvalidOperationException($"Cannot load input type: {job.InputType}");

            var currentVersion = jobType.GetCustomAttribute<SchemaVersionAttribute>()?.Version ?? 1;
            var migratedJson = scope.ServiceProvider
                .GetRequiredService<IMigrationPipeline>()
                .Migrate(job.InputJson, job.SchemaVersion, currentVersion, inputType);

            var input = JsonSerializer.Deserialize(migratedJson, inputType)
                        ?? throw new InvalidOperationException($"Deserialized null input for job {job.Id}.");

            var jobInstance = scope.ServiceProvider.GetRequiredService(jobType);
            var invoker = GetOrBuildInvoker(jobType, inputType);
            var throttleAttrs = jobType.GetCustomAttributes<ThrottleAttribute>(inherit: true);

            return Task.FromResult(new JobInvocationContext(scope, jobInstance, input, invoker, throttleAttrs));
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private static Func<object, object, CancellationToken, Task> GetOrBuildInvoker(
        Type jobType, Type inputType)
    {
        return InvokerCache.GetOrAdd((jobType, inputType), static key =>
        {
            var (jt, it) = key;

            if (it == typeof(NoInput))
            {
                var noInputMethod = jt.GetMethod(
                    nameof(IJob.ExecuteAsync),
                    [typeof(CancellationToken)])!;

                var jobParam = Expression.Parameter(typeof(object), "job");
                var inputParam = Expression.Parameter(typeof(object), "input"); // ignored
                var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

                var call = Expression.Call(
                    Expression.Convert(jobParam, jt),
                    noInputMethod,
                    ctParam);

                return Expression.Lambda<Func<object, object, CancellationToken, Task>>(
                    call, jobParam, inputParam, ctParam).Compile();
            }

            var method = jt.GetMethod(nameof(IJob<object>.ExecuteAsync),
                [it, typeof(CancellationToken)])!;

            var jp = Expression.Parameter(typeof(object), "job");
            var ip = Expression.Parameter(typeof(object), "input");
            var ct = Expression.Parameter(typeof(CancellationToken), "ct");

            var callTyped = Expression.Call(
                Expression.Convert(jp, jt),
                method,
                Expression.Convert(ip, it),
                ct);

            return Expression.Lambda<Func<object, object, CancellationToken, Task>>(
                callTyped, jp, ip, ct).Compile();
        });
    }
}
