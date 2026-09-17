using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace NexJob.Trigger.Salesforce.Tests;

public sealed class SalesforceNexJobExtensionsTests
{
    [Fact]
    public void AddSalesforceTrigger_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();

        // Act
        services.AddSalesforceTrigger(options =>
        {
            options.Topic = "/data/ChangeEvents";
            options.ClientId = "cid";
            options.ClientSecret = "csecret";
        });

        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<SalesforceTriggerOptions>>().Value;
        options.Topic.Should().Be("/data/ChangeEvents");

        provider.GetRequiredService<IReplayIdStore>().Should().NotBeNull();
        provider.GetRequiredService<ISalesforceTokenProvider>().Should().NotBeNull();
        provider.GetRequiredService<ISalesforcePubSubClient>().Should().NotBeNull();
        provider.GetRequiredService<ISalesforceSchemaService>().Should().NotBeNull();

        var hostedServices = provider.GetServices<IHostedService>();
        hostedServices.Should().ContainSingle(s => s is SalesforceTriggerHandler);
    }

    [Fact]
    public void AddSalesforceTrigger_WithGenericJob_RegistersJobType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();

        // Act
        services.AddSalesforceTrigger<CustomSalesforceJob>(options =>
        {
            options.Topic = "/event/OrderEvent__e";
            options.ClientId = "cid";
            options.ClientSecret = "csecret";
        });

        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<SalesforceTriggerOptions>>().Value;
        options.JobType.Should().Be(typeof(CustomSalesforceJob).AssemblyQualifiedName);
        provider.GetRequiredService<CustomSalesforceJob>().Should().NotBeNull();
    }

    [Fact]
    public void AddSalesforceTrigger_ViaNexJobBuilder_RegistersSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddNexJob()
            .AddSalesforceTrigger(options =>
            {
                options.Topic = "/data/AccountChangeEvent";
                options.ClientId = "cid";
                options.ClientSecret = "csecret";
            });

        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<SalesforceTriggerOptions>>().Value;
        options.Topic.Should().Be("/data/AccountChangeEvent");
        provider.GetRequiredService<ISalesforcePubSubClient>().Should().NotBeNull();
    }

    [Fact]
    public void AddSalesforceTrigger_ViaNexJobBuilderGeneric_RegistersSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddNexJob()
            .AddSalesforceTrigger<CustomSalesforceJob>(options =>
            {
                options.Topic = "/data/AccountChangeEvent";
                options.ClientId = "cid";
                options.ClientSecret = "csecret";
            });

        using var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<IOptions<SalesforceTriggerOptions>>().Value;
        options.JobType.Should().Be(typeof(CustomSalesforceJob).AssemblyQualifiedName);
    }

    [Fact]
    public void AddSalesforceTrigger_NullArguments_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var builder = new NexJobBuilder(services);

        var a1 = () => SalesforceNexJobExtensions.AddSalesforceTrigger((IServiceCollection)null!, opt => { });
        var a2 = () => SalesforceNexJobExtensions.AddSalesforceTrigger(services, null!);
        var a3 = () => SalesforceNexJobExtensions.AddSalesforceTrigger((NexJobBuilder)null!, opt => { });
        var a4 = () => builder.AddSalesforceTrigger(null!);

        a1.Should().Throw<ArgumentNullException>();
        a2.Should().Throw<ArgumentNullException>();
        a3.Should().Throw<ArgumentNullException>();
        a4.Should().Throw<ArgumentNullException>();
    }
}

public sealed class CustomSalesforceJob : IJob<string>
{
    public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
}
