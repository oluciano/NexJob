using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using NexJob.Tests;
using Xunit;

namespace NexJob.Trigger.AzureServiceBus.IntegrationTests;

/// <summary>
/// The official Azure Service Bus emulator (it needs a SQL Server) with the queues the tests use.
/// </summary>
public sealed class ServiceBusEmulatorFixture : IAsyncLifetime
{
    /// <summary>A queue the trigger reads (default settings, three deliveries before dead-lettering).</summary>
    public const string MainQueue = "asb-main";

    private const string SqlPassword = "Str0ng!Passw0rd#2026";
    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly int _port = TestPorts.Next();
    private IContainer? _sql;
    private IContainer? _emulator;

    /// <summary>Gets the connection string of the emulator.</summary>
    public string ConnectionString =>
        $"Endpoint=sb://localhost:{_port};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    /// <summary>Gets a queue name for one test: the emulator's queues are created up front, so the pool is fixed.</summary>
    /// <param name="index">The zero-based slot, 0 to 9.</param>
    /// <returns>The queue name.</returns>
    public static string Queue(int index) => $"asb-q{index}";

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        _sql = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithNetwork(_network)
            .WithNetworkAliases("sqlserver")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready for client connections"))
            .Build();
        await _sql.StartAsync();

        _emulator = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-messaging/servicebus-emulator:latest")
            .WithNetwork(_network)
            .WithPortBinding(_port, 5672)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SQL_SERVER", "sqlserver")
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .WithResourceMapping(Encoding.UTF8.GetBytes(BuildConfig()), "/ServiceBus_Emulator/ConfigFiles/Config.json")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Emulator Service is Successfully Up!"))
            .Build();
        await _emulator.StartAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        if (_emulator is not null)
        {
            await _emulator.DisposeAsync();
        }

        if (_sql is not null)
        {
            await _sql.DisposeAsync();
        }

        await _network.DisposeAsync();
    }

    private static string BuildConfig()
    {
        var queues = Enumerable.Range(0, 10).Select(i => Queue(i)).Append(MainQueue).Select(name => new
        {
            Name = name,
            Properties = new
            {
                DeadLetteringOnMessageExpiration = false,
                DefaultMessageTimeToLive = "PT1H",
                DuplicateDetectionHistoryTimeWindow = "PT20S",
                ForwardDeadLetteredMessagesTo = string.Empty,
                ForwardTo = string.Empty,
                LockDuration = "PT1M",
                MaxDeliveryCount = 3,
                RequiresDuplicateDetection = false,
                RequiresSession = false,
            },
        }).ToArray();
        var config = new
        {
            UserConfig = new
            {
                Namespaces = new[] { new { Name = "sbemulatorns", Queues = queues, Topics = Array.Empty<object>() } },
                Logging = new { Type = "File" },
            },
        };
        return System.Text.Json.JsonSerializer.Serialize(config);
    }
}
