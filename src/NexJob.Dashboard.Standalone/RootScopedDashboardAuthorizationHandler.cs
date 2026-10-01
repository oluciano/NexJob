using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NexJob.Dashboard.Standalone;

/// <summary>
/// Bridges an <see cref="IDashboardAuthorizationHandler"/> registered in the parent host into the embedded
/// dashboard server, which has its own service container. The handler is resolved from a new scope of the
/// parent container on every request, so any lifetime (singleton, scoped or transient) works.
/// </summary>
internal sealed class RootScopedDashboardAuthorizationHandler : IDashboardAuthorizationHandler
{
    private readonly IServiceProvider _rootProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="RootScopedDashboardAuthorizationHandler"/> class.
    /// </summary>
    /// <param name="rootProvider">The parent host's service provider.</param>
    public RootScopedDashboardAuthorizationHandler(IServiceProvider rootProvider)
    {
        _rootProvider = rootProvider;
    }

    /// <inheritdoc/>
    public async Task<bool> AuthorizeAsync(HttpContext context)
    {
        var scope = _rootProvider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var handler = scope.ServiceProvider.GetRequiredService<IDashboardAuthorizationHandler>();
            return await handler.AuthorizeAsync(context).ConfigureAwait(false);
        }
    }
}
