using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using Xunit;

namespace NexJob.Tests;

public sealed class DashboardAuthorizationTests
{
    [Fact]
    public async Task Dashboard_WithoutHandler_AllowsAccess()
    {
        // Arrange: no IDashboardAuthorizationHandler registered
        var services = new ServiceCollection()
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };

        // Act: simulate the middleware logic
        var authHandler = context.RequestServices.GetService<IDashboardAuthorizationHandler>();
        var isAuthorized = authHandler is null || await authHandler.AuthorizeAsync(context);

        // Assert: no handler = open access
        isAuthorized.Should().BeTrue("absence of handler must allow access");
    }

    [Fact]
    public async Task Dashboard_HandlerReturnsTrue_AllowsAccess()
    {
        // Arrange: handler that always returns true
        var services = new ServiceCollection()
            .AddSingleton<IDashboardAuthorizationHandler>(new AllowAllHandler())
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };

        // Act
        var authHandler = context.RequestServices.GetService<IDashboardAuthorizationHandler>();
        var isAuthorized = authHandler is not null && await authHandler.AuthorizeAsync(context);

        // Assert
        isAuthorized.Should().BeTrue("handler returned true");
    }

    [Fact]
    public async Task Dashboard_HandlerReturnsFalse_Returns401()
    {
        // Arrange: handler that always returns false
        var services = new ServiceCollection()
            .AddSingleton<IDashboardAuthorizationHandler>(new DenyAllHandler())
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };

        // Act: simulate the middleware logic
        var authHandler = context.RequestServices.GetService<IDashboardAuthorizationHandler>();
        var isAuthorized = authHandler is null || await authHandler.AuthorizeAsync(context);

        // Assert
        isAuthorized.Should().BeFalse("handler returned false, should be unauthorized");
    }

    [Fact]
    public async Task Dashboard_HandlerCanResolveDependencies()
    {
        // Arrange: handler that depends on a service resolved via DI
        var services = new ServiceCollection()
            .AddSingleton<IPermissionChecker, AlwaysTruePermissionChecker>()
            .AddSingleton<IDashboardAuthorizationHandler, DependencyUsingHandler>()
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };

        // Act
        var authHandler = context.RequestServices.GetService<IDashboardAuthorizationHandler>();
        authHandler.Should().NotBeNull();
        var result = await authHandler!.AuthorizeAsync(context);

        // Assert: handler successfully resolved IPermissionChecker from RequestServices
        result.Should().BeTrue("handler resolved dependency and returned true");
    }

    // ─── Test helpers ─────────────────────────────────────────────────────

    private sealed class AllowAllHandler : IDashboardAuthorizationHandler
    {
        public Task<bool> AuthorizeAsync(HttpContext context) => Task.FromResult(true);
    }

    private sealed class DenyAllHandler : IDashboardAuthorizationHandler
    {
        public Task<bool> AuthorizeAsync(HttpContext context) => Task.FromResult(false);
    }

    private sealed class DependencyUsingHandler : IDashboardAuthorizationHandler
    {
        public async Task<bool> AuthorizeAsync(HttpContext context)
        {
            var checker = context.RequestServices.GetRequiredService<IPermissionChecker>();
            return await checker.CanAccessAsync(context);
        }
    }

    private interface IPermissionChecker
    {
        Task<bool> CanAccessAsync(HttpContext context);
    }

    private sealed class AlwaysTruePermissionChecker : IPermissionChecker
    {
        public Task<bool> CanAccessAsync(HttpContext context) => Task.FromResult(true);
    }
}
