using Microsoft.Extensions.DependencyInjection;

namespace NexJob.Internal.Tests;

/// <summary>Builds the job scope a fake <c>IJobInvokerFactory</c> hands to the executor, which resolves its filters from it.</summary>
internal static class TestScopes
{
    /// <summary>Creates a real DI scope that serves the given filters.</summary>
    /// <param name="filters">The filters the job scope resolves.</param>
    /// <returns>The scope.</returns>
    internal static IServiceScope Create(params IJobExecutionFilter[] filters)
    {
        var services = new ServiceCollection();
        foreach (var filter in filters)
        {
            services.AddSingleton(filter);
        }

        return services.BuildServiceProvider().CreateScope();
    }
}
