using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Sample.Providers;
using Xunit;

namespace NexJob.Samples.Tests;

/// <summary>The Providers sample picks its storage from configuration and fails with a clear message when it cannot (issue #288).</summary>
public sealed class ProvidersSampleTests : IClassFixture<WebApplicationFactory<EchoJob>>
{
    private readonly WebApplicationFactory<EchoJob> _factory;

    public ProvidersSampleTests(WebApplicationFactory<EchoJob> factory) => _factory = factory;

    [Fact]
    public async Task InMemoryByDefault_RunsAJobEndToEnd()
    {
        // N1
        var client = _factory.CreateClient();
        (await client.GetFromJsonAsync<JsonElement>("/provider")).GetProperty("provider").GetString().Should().Be("InMemory");

        var accepted = await client.PostAsync("/jobs?message=hello", null);
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        string? status;
        do
        {
            await Task.Delay(100);
            status = (await client.GetFromJsonAsync<JsonElement>($"/jobs/{id}")).GetProperty("status").GetString();
        }
        while (status != "Succeeded" && DateTime.UtcNow < deadline);

        status.Should().Be("Succeeded");
    }

    [Theory]
    [InlineData("Postgres", "NexJobPostgres")]
    [InlineData("SqlServer", "NexJobSqlServer")]
    [InlineData("Redis", "NexJobRedis")]
    [InlineData("MongoDB", "NexJobMongoDB")]
    public void ProviderWithoutItsConnectionString_FailsWithAMessageNamingTheKey(string provider, string key)
    {
        // N2
        var act = () => ProviderSetup.Register(new ServiceCollection(), Config(("Sample:Provider", provider)));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*ConnectionStrings:{key}*");
    }

    [Fact]
    public void UnknownProvider_ListsTheSupportedOnes()
    {
        // N3
        var act = () => ProviderSetup.Register(new ServiceCollection(), Config(("Sample:Provider", "Oracle")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Oracle*InMemory, Postgres, SqlServer, Redis, MongoDB*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("inmemory")]
    public void MissingOrLowerCaseProvider_MeansInMemory(string? configured)
    {
        // N3 (boundary)
        var config = configured is null ? Config() : Config(("Sample:Provider", configured));

        ProviderSetup.Register(new ServiceCollection(), config).Should().Be("InMemory");
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();
}
