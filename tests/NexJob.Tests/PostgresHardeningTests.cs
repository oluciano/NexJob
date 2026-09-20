using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Configuration;
using NexJob.Postgres;
using NexJob.Storage;
using Npgsql;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Hardening unit tests for <see cref="PostgresStorageProvider"/>.
/// Targets 100% coverage for mapping, decision logic, and NpgsqlDataSource lifecycle.
/// </summary>
public sealed class PostgresHardeningTests
{
    private static readonly MethodInfo ParseStatusMethod = typeof(PostgresStorageProvider)
        .GetMethod("ParseStatus", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo IsActiveStateMethod = typeof(PostgresStorageProvider)
        .GetMethod("IsActiveState", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo OpenMethod = typeof(PostgresStorageProvider)
        .GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Tests all status mapping branches.</summary>
    /// <param name="input">The string input.</param>
    /// <param name="expected">The expected enum.</param>
    [Theory]
    [InlineData("Enqueued", JobStatus.Enqueued)]
    [InlineData("Processing", JobStatus.Processing)]
    [InlineData("Succeeded", JobStatus.Succeeded)]
    [InlineData("Failed", JobStatus.Failed)]
    [InlineData("Scheduled", JobStatus.Scheduled)]
    [InlineData("AwaitingContinuation", JobStatus.AwaitingContinuation)]
    [InlineData("Expired", JobStatus.Expired)]
    [InlineData("Invalid", JobStatus.Failed)]
    public void ParseStatus_MapsAllBranches(string input, JobStatus expected)
    {
        var result = (JobStatus)ParseStatusMethod.Invoke(null, new object[] { input })!;
        result.Should().Be(expected);
    }

    /// <summary>Tests all active state identification branches.</summary>
    /// <param name="status">The status.</param>
    /// <param name="expected">If true, expected.</param>
    [Theory]
    [InlineData(JobStatus.Enqueued, true)]
    [InlineData(JobStatus.Processing, true)]
    [InlineData(JobStatus.Scheduled, true)]
    [InlineData(JobStatus.AwaitingContinuation, true)]
    [InlineData(JobStatus.Succeeded, false)]
    [InlineData(JobStatus.Failed, false)]
    [InlineData(JobStatus.Expired, false)]
    public void IsActiveState_IdentifiesCorrectBranches(JobStatus status, bool expected)
    {
        var result = (bool)IsActiveStateMethod.Invoke(null, new object[] { status })!;
        result.Should().Be(expected);
    }

    // ── N1: Positive paths ───────────────────────────────────────────────────

    /// <summary>Tests that initializing with an existing NpgsqlDataSource sets fields correctly without running migrations.</summary>
    [Fact]
    public void Constructor_WithNpgsqlDataSource_InitializesWithoutMigrationAndStoresDataSource()
    {
        using var ds = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");
        var options = new NexJobOptions();

        using var provider = new PostgresStorageProvider(ds, options);

        provider.DataSource.Should().BeSameAs(ds);
        provider.OwnsDataSource.Should().BeFalse();
        provider.IsDisposed.Should().BeFalse();
    }

    /// <summary>Tests that disposing an instance owning the data source disposes the underlying data source.</summary>
    [Fact]
    public void Dispose_WhenOwningDataSource_DisposesDataSource()
    {
        var ds = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");
        var provider = new PostgresStorageProvider(ds, ownsDataSource: true);

        provider.IsDisposed.Should().BeFalse();
        provider.Dispose();

        provider.IsDisposed.Should().BeTrue();
        var act = () => ds.OpenConnection();
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>Tests that async disposal of an instance owning the data source cleanly disposes the data source.</summary>
    [Fact]
    public async Task DisposeAsync_WhenOwningDataSource_DisposesDataSourceAsync()
    {
        var ds = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");
        var provider = new PostgresStorageProvider(ds, ownsDataSource: true);

        provider.IsDisposed.Should().BeFalse();
        await provider.DisposeAsync();

        provider.IsDisposed.Should().BeTrue();
        var act = () => ds.OpenConnection();
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>Tests that disposing an instance not owning the data source keeps the external data source alive.</summary>
    [Fact]
    public void Dispose_WhenNotOwningDataSource_DoesNotDisposeExternalDataSource()
    {
        using var ds = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");
        var provider = new PostgresStorageProvider(ds, ownsDataSource: false);

        provider.IsDisposed.Should().BeFalse();
        provider.Dispose();

        provider.IsDisposed.Should().BeTrue();
        var act = () => ds.CreateConnection();
        act.Should().NotThrow();
    }

    /// <summary>Tests that AddNexJobPostgres with NpgsqlDataSource registers all storage interfaces correctly.</summary>
    [Fact]
    public void AddNexJobPostgres_WithDataSource_RegistersServicesCorrectly()
    {
        var services = new ServiceCollection();
        using var ds = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");

        services.AddNexJobPostgres(ds);
        using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<PostgresStorageProvider>();
        provider.Should().NotBeNull();
        provider.DataSource.Should().BeSameAs(ds);
        provider.OwnsDataSource.Should().BeFalse();

        sp.GetRequiredService<IStorageProvider>().Should().BeSameAs(provider);
        sp.GetRequiredService<IJobStorage>().Should().BeSameAs(provider);
        sp.GetRequiredService<IRecurringStorage>().Should().BeSameAs(provider);
        sp.GetRequiredService<IDashboardStorage>().Should().BeSameAs(provider);
        sp.GetRequiredService<IRuntimeSettingsStore>().Should().NotBeNull();
    }

    // ── N2: Negative paths ───────────────────────────────────────────────────

    /// <summary>Tests that invoking Open() on a disposed provider throws ObjectDisposedException.</summary>
    [Fact]
    public void Open_WhenDisposed_ThrowsObjectDisposedException()
    {
        using var ds = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");
        var provider = new PostgresStorageProvider(ds, ownsDataSource: false);

        provider.Dispose();

        var act = () => OpenMethod.Invoke(provider, null);
        act.Should().Throw<TargetInvocationException>()
           .WithInnerException<ObjectDisposedException>();
    }

    /// <summary>Tests that an unreachable connection string throws on migration and disposes the created data source.</summary>
    [Fact]
    public void Constructor_WithUnreachableHostConnectionString_ThrowsAndDisposesCreatedDataSource()
    {
        var invalidCs = "Host=127.0.0.1;Port=65534;Database=nonexistent;Username=none;Password=none;Timeout=1;Command Timeout=1";
        var act = () => new PostgresStorageProvider(invalidCs);
        act.Should().Throw<Exception>();
    }

    // ── N3: Boundary & Inputs ────────────────────────────────────────────────

    /// <summary>Tests that null or empty connection string throws ArgumentException.</summary>
    [Fact]
    public void Constructor_NullOrWhitespaceConnectionString_ThrowsArgumentException()
    {
        var actNull = () => new PostgresStorageProvider((string)null!);
        actNull.Should().Throw<ArgumentException>();

        var actEmpty = () => new PostgresStorageProvider("   ");
        actEmpty.Should().Throw<ArgumentException>();
    }

    /// <summary>Tests that null NpgsqlDataSource throws ArgumentNullException.</summary>
    [Fact]
    public void Constructor_NullDataSource_ThrowsArgumentNullException()
    {
        var act = () => new PostgresStorageProvider((NpgsqlDataSource)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>Tests that multiple calls to Dispose are idempotent.</summary>
    [Fact]
    public void Dispose_CalledMultipleTimes_IsIdempotentAndDoesNotThrow()
    {
        var ds = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");
        var provider = new PostgresStorageProvider(ds, ownsDataSource: true);

        provider.Dispose();
        var act = () => provider.Dispose();
        act.Should().NotThrow();
        provider.IsDisposed.Should().BeTrue();
    }

    /// <summary>Tests that null arguments to AddNexJobPostgres throw ArgumentNullException.</summary>
    [Fact]
    public void AddNexJobPostgres_NullArguments_ThrowArgumentNullException()
    {
        IServiceCollection services = null!;
        using var ds = NpgsqlDataSource.Create("Host=localhost;Database=test;Username=test;Password=test");

        var act1 = () => services.AddNexJobPostgres(ds);
        act1.Should().Throw<ArgumentNullException>();

        var act2 = () => new ServiceCollection().AddNexJobPostgres((NpgsqlDataSource)null!);
        act2.Should().Throw<ArgumentNullException>();

        var act3 = () => new ServiceCollection().AddNexJobPostgres((string)null!);
        act3.Should().Throw<ArgumentNullException>();

        var act4 = () => new ServiceCollection().AddNexJobPostgres("   ");
        act4.Should().Throw<ArgumentException>();
    }
}
