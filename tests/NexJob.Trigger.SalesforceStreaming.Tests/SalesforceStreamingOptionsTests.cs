using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Xunit;

namespace NexJob.Trigger.SalesforceStreaming.Tests;

public sealed class SalesforceStreamingOptionsTests
{
    [Fact]
    public void Defaults_AreProperlyInitialized()
    {
        // Act
        var options = new SalesforceStreamingTriggerOptions();

        // Assert
        options.Channel.Should().BeEmpty();
        options.TargetQueue.Should().Be("salesforce-streaming");
        options.JobPriority.Should().Be(JobPriority.Normal);
        options.ReplayPreset.Should().Be(SalesforceStreamingReplayPreset.Latest);
        options.CustomReplayId.Should().BeNull();
        options.ReplayIdStore.Should().BeNull();
        options.ReplayStoreDirectory.Should().Be("./.nexjob/salesforce-streaming");
        options.ReconnectDelay.Should().Be(TimeSpan.FromSeconds(5));
        options.MaxReconnectDelay.Should().Be(TimeSpan.FromMinutes(1));
        options.MaxRetries.Should().Be(5);
        options.DeadLetterQueue.Should().BeNull();
        options.CometdVersion.Should().Be("60.0");
        options.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(120));
        options.JobType.Should().BeNull();
        options.Authentication.Should().NotBeNull();
        options.Authentication.AuthType.Should().Be(SalesforceStreamingAuthType.OAuth2UsernamePassword);
        options.Authentication.AuthEndpoint.Should().Be("https://login.salesforce.com/services/oauth2/token");
    }

    [Fact]
    public void Validate_ValidOAuth2UsernamePasswordOptions_PassesValidation()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.OAuth2UsernamePassword,
                ClientId = "client-123",
                ClientSecret = "secret-456",
                Username = "user@domain.com",
                Password = "pwd",
                SecurityToken = "token",
            },
        };

        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ValidClientCredentialsOptions_PassesValidation()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/event/Notification__e",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials,
                ClientId = "client-123",
                ClientSecret = "secret-456",
            },
        };

        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ValidSessionIdOptions_PassesValidation()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/topic/InvoiceUpdates",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://na1.salesforce.com",
                AccessToken = "session-id-123",
            },
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
    public void Validate_MissingChannel_FailsValidation(string? channel)
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = channel!,
            Authentication = CreateValidAuth(),
        };

        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingTriggerOptions.Channel)));
    }

    [Theory]
    [InlineData("data/Order__ChangeEvent")]
    [InlineData("topic/Updates")]
    public void Validate_ChannelWithoutLeadingSlash_FailsValidation(string channel)
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = channel,
            Authentication = CreateValidAuth(),
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingTriggerOptions.Channel)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_EmptyTargetQueue_FailsValidation(string targetQueue)
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Orders",
            TargetQueue = targetQueue,
            Authentication = CreateValidAuth(),
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingTriggerOptions.TargetQueue)));
    }

    [Fact]
    public void Validate_ZeroOrNegativeReconnectDelay_FailsValidation()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Orders",
            ReconnectDelay = TimeSpan.Zero,
            Authentication = CreateValidAuth(),
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingTriggerOptions.ReconnectDelay)));
    }

    [Fact]
    public void Validate_MaxReconnectDelayLessThanReconnectDelay_FailsValidation()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Orders",
            ReconnectDelay = TimeSpan.FromSeconds(10),
            MaxReconnectDelay = TimeSpan.FromSeconds(5),
            Authentication = CreateValidAuth(),
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingTriggerOptions.MaxReconnectDelay)));
    }

    [Fact]
    public void Validate_CustomReplayPresetWithoutCustomReplayId_FailsValidation()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Orders",
            ReplayPreset = SalesforceStreamingReplayPreset.Custom,
            CustomReplayId = null,
            Authentication = CreateValidAuth(),
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingTriggerOptions.CustomReplayId)));
    }

    [Fact]
    public void Validate_CustomReplayPresetWithCustomReplayId_PassesValidation()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Orders",
            ReplayPreset = SalesforceStreamingReplayPreset.Custom,
            CustomReplayId = 123456,
            Authentication = CreateValidAuth(),
        };

        var context = new ValidationContext(options);
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    public void Validate_InvalidAuthEndpoint_FailsValidation(string endpoint)
    {
        // Arrange
        var auth = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2UsernamePassword,
            AuthEndpoint = endpoint,
            ClientId = "id",
            ClientSecret = "sec",
            Username = "u",
            Password = "p",
        };

        var results = auth.Validate(new ValidationContext(auth)).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingAuthOptions.AuthEndpoint)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_MissingOAuthCredentials_FailsValidation(string? emptyVal)
    {
        // Arrange
        var auth = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2UsernamePassword,
            ClientId = emptyVal,
            ClientSecret = emptyVal,
            Username = emptyVal,
            Password = emptyVal,
        };

        var results = auth.Validate(new ValidationContext(auth)).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingAuthOptions.ClientId)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingAuthOptions.ClientSecret)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingAuthOptions.Username)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingAuthOptions.Password)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_MissingSessionIdParameters_FailsValidation(string? emptyVal)
    {
        // Arrange
        var auth = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.SessionId,
            InstanceUrl = emptyVal,
            AccessToken = emptyVal,
        };

        var results = auth.Validate(new ValidationContext(auth)).ToList();

        // Assert
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingAuthOptions.InstanceUrl)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SalesforceStreamingAuthOptions.AccessToken)));
    }

    private static SalesforceStreamingAuthOptions CreateValidAuth() => new()
    {
        AuthType = SalesforceStreamingAuthType.OAuth2UsernamePassword,
        ClientId = "client",
        ClientSecret = "secret",
        Username = "user",
        Password = "pwd",
    };
}
