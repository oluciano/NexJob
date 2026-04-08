using Microsoft.AspNetCore.Http;

namespace NexJob;

/// <summary>
/// Defines the authorization logic for the NexJob dashboard.
/// Implement this interface and register it in the DI container to restrict
/// access to the dashboard. If no implementation is registered, the dashboard
/// is accessible to everyone (suitable for development and internal networks).
/// </summary>
/// <example>
/// <code>
/// // Role-based (ASP.NET Core Identity / AD / Entra ID)
/// public class DashboardAuth : IDashboardAuthorizationHandler
/// {
///     public Task&lt;bool&gt; AuthorizeAsync(HttpContext context) =&gt;
///         Task.FromResult(context.User.IsInRole("ops"));
/// }
///
/// // API key
/// public class DashboardAuth : IDashboardAuthorizationHandler
/// {
///     public Task&lt;bool&gt; AuthorizeAsync(HttpContext context) =&gt;
///         Task.FromResult(context.Request.Headers["X-Api-Key"] == "my-secret");
/// }
///
/// // Custom service via DI
/// public class DashboardAuth : IDashboardAuthorizationHandler
/// {
///     public async Task&lt;bool&gt; AuthorizeAsync(HttpContext context)
///     {
///         var svc = context.RequestServices.GetRequiredService&lt;IPermissionService&gt;();
///         return await svc.CanAccessDashboardAsync(context.User);
///     }
/// }
///
/// // Registration
/// services.AddSingleton&lt;IDashboardAuthorizationHandler, DashboardAuth&gt;();
/// </code>
/// </example>
public interface IDashboardAuthorizationHandler
{
    /// <summary>
    /// Determines whether the current request is authorized to access the dashboard.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>
    /// <see langword="true"/> to allow access; <see langword="false"/> to return
    /// <c>401 Unauthorized</c>.
    /// </returns>
    Task<bool> AuthorizeAsync(HttpContext context);
}
