using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Xunit;

namespace NexJob.Trigger.Salesforce.Tests;

public sealed class SalesforceTriggerOptionsTests
{
    [Fact]
    public void Defaults_AreProperlyInitialized()
    {
        // Act
        var options = new SalesforceTriggerOptions();

        // Assert
        options.TargetQueue.Should().Be("salesforce-events");
        options.JobPriority.Should().Be(JobPriority.Normal);
        options.AuthEndpoint.Should().Be("https://login.salesforce.com/services/oauth2/token");
        options.PubSubEndpoint.Should().Be("api.pubsub.salesforce.com:7443");
        options.ReplayPreset.Should().Be(SalesforceReplayPreset.Latest);
        options.FallbackPolicy.Should().Be(ReplayFallbackPolicy.FailFast);
        options.BatchSize.Should().Be(100);
        options.ReplayStoreDirectory.Should().Be("./.nexjob/salesforce");
        options.CustomReplayId.Should().BeNull();
        options.TenantId.Should().BeNull();
        options.JobType.Should().BeNull();
        options.DeadLetterQueue.Should().BeNull();
    }

    [Fact]
    public void Validate_ValidOptions_PassesValidation()
    {
        // Arrange
        var options = new SalesforceTriggerOptions
        {
            Topic = "/data/ChangeEvents",
            ClientId = "client-id-123",
            ClientSecret = "client-secret-xyz",
            AuthEndpoint = "https://login.salesforce.com/services/oauth2/token",
            PubSubEndpoint = "api.pubsub.salesforce.com:7443",
        };

        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_MissingTopic_FailsValidation(string? topic)
    {
        // Arrange
        var options = new SalesforceTriggerOptions
        {
            Topic = topic!,
            ClientId = "client-id-123",
            ClientSecret = "client-secret-xyz",
        };

        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceTriggerOptions.Topic)));
    }

    [Theory]
    [InlineData("data/ChangeEvents")]
    [InlineData("ChangeEvents")]
    public void Validate_TopicWithoutLeadingSlash_FailsValidation(string topic)
    {
        // Arrange
        var options = new SalesforceTriggerOptions
        {
            Topic = topic,
            ClientId = "client-id-123",
            ClientSecret = "client-secret-xyz",
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceTriggerOptions.Topic)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_MissingClientId_FailsValidation(string? clientId)
    {
        // Arrange
        var options = new SalesforceTriggerOptions
        {
            Topic = "/data/ChangeEvents",
            ClientId = clientId!,
            ClientSecret = "client-secret-xyz",
        };

        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceTriggerOptions.ClientId)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_MissingClientSecret_FailsValidation(string? clientSecret)
    {
        // Arrange
        var options = new SalesforceTriggerOptions
        {
            Topic = "/data/ChangeEvents",
            ClientId = "client-id-123",
            ClientSecret = clientSecret!,
        };

        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceTriggerOptions.ClientSecret)));
    }

    [Theory]
    [InlineData("invalid-url")]
    [InlineData("ftp://login.salesforce.com")]
    public void Validate_InvalidAuthEndpoint_FailsValidation(string authEndpoint)
    {
        // Arrange
        var options = new SalesforceTriggerOptions
        {
            Topic = "/data/ChangeEvents",
            ClientId = "client-id-123",
            ClientSecret = "client-secret-xyz",
            AuthEndpoint = authEndpoint,
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceTriggerOptions.AuthEndpoint)));
    }

    [Fact]
    public void Validate_CustomReplayPresetWithoutCustomReplayId_FailsValidation()
    {
        // Arrange
        var options = new SalesforceTriggerOptions
        {
            Topic = "/data/ChangeEvents",
            ClientId = "client-id-123",
            ClientSecret = "client-secret-xyz",
            ReplayPreset = SalesforceReplayPreset.Custom,
            CustomReplayId = null,
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceTriggerOptions.CustomReplayId)));
    }

    [Fact]
    public void Validate_CustomReplayPresetWithEmptyCustomReplayId_FailsValidation()
    {
        // Arrange
        var options = new SalesforceTriggerOptions
        {
            Topic = "/data/ChangeEvents",
            ClientId = "client-id-123",
            ClientSecret = "client-secret-xyz",
            ReplayPreset = SalesforceReplayPreset.Custom,
            CustomReplayId = [],
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceTriggerOptions.CustomReplayId)));
    }

    [Fact]
    public void Validate_CustomReplayPresetWithCustomReplayId_PassesValidation()
    {
        // Arrange
        var options = new SalesforceTriggerOptions
        {
            Topic = "/data/ChangeEvents",
            ClientId = "client-id-123",
            ClientSecret = "client-secret-xyz",
            ReplayPreset = SalesforceReplayPreset.Custom,
            CustomReplayId = [0x01, 0x02, 0x03],
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://login.salesforce.com/services/oauth2/token")]
    [InlineData("http://example.com")]
    public void Validate_AuthEndpointThatIsHttpAndNotLoopback_FailsAndNamesHttps(string authEndpoint)
    {
        var options = ValidOptions(authEndpoint);

        var results = options.Validate(new ValidationContext(options)).ToList();

        results.Should().ContainSingle(r => r.MemberNames.Contains(nameof(SalesforceTriggerOptions.AuthEndpoint)))
            .Which.ErrorMessage.Should().Contain("https");
    }

    [Theory]
    [InlineData("https://login.salesforce.com/services/oauth2/token")]
    [InlineData("HTTPS://login.salesforce.com/services/oauth2/token")]
    [InlineData("http://localhost:8080/services/oauth2/token")]
    [InlineData("http://127.0.0.1:5000/token")]
    [InlineData("http://[::1]:5000/token")]
    [InlineData("HTTP://LOCALHOST/token")]
    public void Validate_HttpsOrLoopbackAuthEndpoint_HasNoAuthEndpointError(string authEndpoint)
    {
        var options = ValidOptions(authEndpoint);

        var results = options.Validate(new ValidationContext(options)).ToList();

        results.Should().NotContain(r => r.MemberNames.Contains(nameof(SalesforceTriggerOptions.AuthEndpoint)));
    }

    private static SalesforceTriggerOptions ValidOptions(string authEndpoint) => new()
    {
        Topic = "/data/ChangeEvents",
        ClientId = "client-id-123",
        ClientSecret = "client-secret-xyz",
        AuthEndpoint = authEndpoint,
    };
}
