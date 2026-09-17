using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace NexJob.Trigger.SalesforceStreaming.Tests;

public sealed class SalesforceStreamingNexJobExtensionsTests
{
    [Fact]
    public void AddSalesforceStreamingTrigger_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();

        // Act
        services.AddSalesforceStreamingTrigger(options =>
        {
            options.Channel = "/topic/InvoiceUpdates";
            options.Authentication.AuthType = SalesforceStreamingAuthType.SessionId;
            options.Authentication.InstanceUrl = "https://example.my.salesforce.com";
            options.Authentication.SessionId = "token-123";
        });

        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<SalesforceStreamingTriggerOptions>>().Value;
        options.Channel.Should().Be("/topic/InvoiceUpdates");
        options.JobType.Should().Be(typeof(SalesforceStreamingEventJob));

        provider.GetRequiredService<ISalesforceStreamingAuthService>().Should().NotBeNull();
        provider.GetRequiredService<ISalesforceBayeuxClient>().Should().NotBeNull();
        provider.GetRequiredService<IStreamingReplayIdStore>().Should().NotBeNull();

        var hostedServices = provider.GetServices<IHostedService>();
        hostedServices.Should().ContainSingle(s => s is SalesforceStreamingTriggerHandler);
    }

    [Fact]
    public void AddNexJobSalesforceStreamingTrigger_WithCustomJob_RegistersJobType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();

        // Act
        services.AddNexJobSalesforceStreamingTrigger<CustomStreamingJob>(options =>
        {
            options.Channel = "/event/OrderEvent__e";
            options.Authentication.AuthType = SalesforceStreamingAuthType.SessionId;
            options.Authentication.InstanceUrl = "https://example.my.salesforce.com";
            options.Authentication.SessionId = "token-456";
        });

        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<SalesforceStreamingTriggerOptions>>().Value;
        options.JobType.Should().Be(typeof(CustomStreamingJob));
        provider.GetRequiredService<CustomStreamingJob>().Should().NotBeNull();
    }

    [Fact]
    public void AddSalesforceStreamingTrigger_WithExplicitReplayIdStore_ResolvesConfiguredStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();
        var customStore = new InMemoryStreamingReplayIdStore();

        // Act
        services.AddSalesforceStreamingTrigger(options =>
        {
            options.Channel = "/data/AccountChangeEvent";
            options.Authentication.AuthType = SalesforceStreamingAuthType.SessionId;
            options.Authentication.InstanceUrl = "https://example.my.salesforce.com";
            options.Authentication.SessionId = "token-xyz";
            options.ReplayIdStore = customStore;
        });

        using var provider = services.BuildServiceProvider();

        // Assert
        var store = provider.GetRequiredService<IStreamingReplayIdStore>();
        store.Should().BeSameAs(customStore);
    }

    [Fact]
    public void AddSalesforceStreamingTrigger_ViaNexJobBuilder_RegistersSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddNexJob()
            .AddSalesforceStreamingTrigger(options =>
            {
                options.Channel = "/topic/CustomerUpdates";
                options.Authentication.AuthType = SalesforceStreamingAuthType.SessionId;
                options.Authentication.InstanceUrl = "https://example.my.salesforce.com";
                options.Authentication.SessionId = "token-789";
            });

        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<SalesforceStreamingTriggerOptions>>().Value;
        options.Channel.Should().Be("/topic/CustomerUpdates");
        provider.GetRequiredService<ISalesforceBayeuxClient>().Should().NotBeNull();
    }

    [Fact]
    public void AddSalesforceStreamingTrigger_ViaNexJobBuilderGeneric_RegistersSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddNexJob()
            .AddSalesforceStreamingTrigger<CustomStreamingJob>(options =>
            {
                options.Channel = "/event/PaymentEvent__e";
                options.Authentication.AuthType = SalesforceStreamingAuthType.SessionId;
                options.Authentication.InstanceUrl = "https://example.my.salesforce.com";
                options.Authentication.SessionId = "token-abc";
            });

        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<SalesforceStreamingTriggerOptions>>().Value;
        options.JobType.Should().Be(typeof(CustomStreamingJob));
        provider.GetRequiredService<CustomStreamingJob>().Should().NotBeNull();
    }

    [Fact]
    public void AddSalesforceStreamingTrigger_InvalidOptions_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();

        services.AddSalesforceStreamingTrigger(options =>
        {
            options.Channel = "invalid-channel-no-slash";
            options.Authentication.AuthType = SalesforceStreamingAuthType.SessionId;
            options.Authentication.InstanceUrl = "https://example.my.salesforce.com";
            options.Authentication.SessionId = "token";
        });

        using var provider = services.BuildServiceProvider();

        // Act
        var act = () => provider.GetRequiredService<IOptions<SalesforceStreamingTriggerOptions>>().Value;

        // Assert
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddSalesforceStreamingTrigger_NullArguments_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var builder = new NexJobBuilder(services);

        var a1 = () => SalesforceStreamingNexJobExtensions.AddNexJobSalesforceStreamingTrigger((IServiceCollection)null!, opt => { });
        var a2 = () => SalesforceStreamingNexJobExtensions.AddNexJobSalesforceStreamingTrigger(services, null!);
        var a3 = () => SalesforceStreamingNexJobExtensions.AddSalesforceStreamingTrigger((IServiceCollection)null!, opt => { });
        var a4 = () => SalesforceStreamingNexJobExtensions.AddSalesforceStreamingTrigger(services, null!);
        var a5 = () => SalesforceStreamingNexJobExtensions.AddSalesforceStreamingTrigger((NexJobBuilder)null!, opt => { });
        var a6 = () => builder.AddSalesforceStreamingTrigger(null!);
        var a7 = () => SalesforceStreamingNexJobExtensions.AddSalesforceStreamingTrigger<CustomStreamingJob>((NexJobBuilder)null!, opt => { });
        var a8 = () => builder.AddSalesforceStreamingTrigger<CustomStreamingJob>(null!);

        a1.Should().Throw<ArgumentNullException>();
        a2.Should().Throw<ArgumentNullException>();
        a3.Should().Throw<ArgumentNullException>();
        a4.Should().Throw<ArgumentNullException>();
        a5.Should().Throw<ArgumentNullException>();
        a6.Should().Throw<ArgumentNullException>();
        a7.Should().Throw<ArgumentNullException>();
        a8.Should().Throw<ArgumentNullException>();
    }
}

public sealed class CustomStreamingJob : IJob<SalesforceStreamingEventInput>
{
    public Task ExecuteAsync(SalesforceStreamingEventInput input, CancellationToken cancellationToken) => Task.CompletedTask;
}
