using FluentAssertions;
using Microsoft.Data.SqlClient;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// <c>SqlServerStorageProvider(SqlConnection, NexJobOptions)</c> keeps the connection's connection string and opens its own
/// new connections with it. SqlClient removes the password from <c>ConnectionString</c> once a connection is opened (unless
/// <c>Persist Security Info=True</c>), so what the provider can use depends on the state of the connection it is given.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerConnectionObjectTests(SqlServerReliabilityFixture fixture) : IClassFixture<SqlServerReliabilityFixture>
{
    // N1 (Positive): a connection that was never opened keeps its full connection string (this is the read-replica path).
    [Fact]
    public async Task ClosedConnection_KeepsItsCredentials_AndTheProviderWorks()
    {
        PrepareSchema();
        await using var connection = new SqlConnection(fixture.ConnectionString);

        var provider = new SqlServerStorageProvider(connection, new NexJobOptions());

        Func<Task> act = () => provider.EnqueueAsync(NewJob());
        await act.Should().NotThrowAsync();
    }

    // N1: with Persist Security Info the password survives opening, so an opened connection is fine too.
    [Fact]
    public async Task OpenedConnection_WithPersistSecurityInfo_KeepsItsCredentials_AndTheProviderWorks()
    {
        PrepareSchema();
        var builder = new SqlConnectionStringBuilder(fixture.ConnectionString) { PersistSecurityInfo = true };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        var provider = new SqlServerStorageProvider(connection, new NexJobOptions());

        Func<Task> act = () => provider.EnqueueAsync(NewJob());
        await act.Should().NotThrowAsync();
    }

    // N2 (Negative): an opened connection has lost the password; failing at construction with a clear message beats a login
    // failure much later, far from the cause.
    [Fact]
    public async Task OpenedConnection_WithoutThePassword_IsRefusedWithAClearMessage()
    {
        PrepareSchema();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        connection.ConnectionString.Should().NotContainEquivalentOf("Password", "SqlClient removes it once the connection is open");

        Action act = () => _ = new SqlServerStorageProvider(connection, new NexJobOptions());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*already open*")
            .WithMessage("*Persist Security Info*");
    }

    // N3 (Invalid input): integrated security has no password to lose, so an opened connection is fine.
    [Fact]
    public void OpenedConnection_WithIntegratedSecurity_IsNotRefused()
    {
        var builder = new SqlConnectionStringBuilder { DataSource = "localhost", InitialCatalog = "nexjob", IntegratedSecurity = true };
        using var connection = new SqlConnection(builder.ConnectionString);

        Action act = () => _ = new SqlServerStorageProvider(connection, new NexJobOptions());

        act.Should().NotThrow();
    }

    [Fact]
    public void NullConnection_IsRejected()
    {
        Action act = () => _ = new SqlServerStorageProvider((SqlConnection)null!, new NexJobOptions());

        act.Should().Throw<ArgumentNullException>();
    }

    private static JobRecord NewJob() => new()
    {
        Id = new JobId(Guid.NewGuid()),
        JobType = "t",
        InputType = "t",
        InputJson = "{}",
        Queue = $"conn-object-{Guid.NewGuid():N}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // The constructor under test does not migrate, so the schema is created by the usual provider first.
    private void PrepareSchema() => _ = new SqlServerStorageProvider(fixture.ConnectionString);
}
