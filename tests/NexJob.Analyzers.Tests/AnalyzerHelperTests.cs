using System;
using Microsoft.CodeAnalysis;
using Xunit;

namespace NexJob.Analyzers.Tests;

/// <summary>
/// Unit tests for AnalyzerHelper descriptor and help link generation.
/// </summary>
public sealed class AnalyzerHelperTests
{
    // N1 - Positive: HelpLinkUri points to the dedicated per-rule anchor in lower-case
    [Theory]
    [InlineData("NXJ001", "https://oluciano.github.io/NexJob/guides/analyzers/#nxj001")]
    [InlineData("NXJ005", "https://oluciano.github.io/NexJob/guides/analyzers/#nxj005")]
    [InlineData("NXJ019", "https://oluciano.github.io/NexJob/guides/analyzers/#nxj019")]
    public void CreateDescriptor_ValidId_GeneratesExpectedHelpLinkUri(string diagnosticId, string expectedUrl)
    {
        var descriptor = AnalyzerHelper.CreateDescriptor(
            id: diagnosticId,
            title: "Test Title",
            messageFormat: "Test Message",
            category: "Reliability",
            description: "Test Description");

        Assert.Equal(expectedUrl, descriptor.HelpLinkUri);
        Assert.Equal(diagnosticId, descriptor.Id);
        Assert.True(descriptor.IsEnabledByDefault);
    }

    // N2 - Negative: Opt-in rule disabled by default produces valid per-rule help link
    [Fact]
    public void CreateDescriptor_OptInRule_GeneratesHelpLinkUriAndDisabledByDefault()
    {
        var descriptor = AnalyzerHelper.CreateDescriptor(
            id: "NXJ011",
            title: "Idempotency",
            messageFormat: "Consider idempotency key",
            category: "Design",
            description: "Opt-in idempotency",
            isEnabledByDefault: false);

        Assert.Equal("https://oluciano.github.io/NexJob/guides/analyzers/#nxj011", descriptor.HelpLinkUri);
        Assert.False(descriptor.IsEnabledByDefault);
    }

    // N3 - Boundary / Invalid Input: Null, empty, or whitespace diagnostic ID handled safely
    [Theory]
    [InlineData(null, "https://oluciano.github.io/NexJob/guides/analyzers/")]
    [InlineData("", "https://oluciano.github.io/NexJob/guides/analyzers/")]
    [InlineData("   ", "https://oluciano.github.io/NexJob/guides/analyzers/")]
    public void GetHelpLinkUri_NullOrEmptyId_FallsBackToBaseHelpUrl(string? diagnosticId, string expectedUrl)
    {
        var helpLink = AnalyzerHelper.GetHelpLinkUri(diagnosticId);
        Assert.Equal(expectedUrl, helpLink);
    }

    [Theory]
    [InlineData("NXJ001", "https://oluciano.github.io/NexJob/guides/analyzers/#nxj001")]
    [InlineData("nxj018", "https://oluciano.github.io/NexJob/guides/analyzers/#nxj018")]
    public void GetHelpLinkUri_ValidId_GeneratesPerRuleAnchor(string diagnosticId, string expectedUrl)
    {
        var helpLink = AnalyzerHelper.GetHelpLinkUri(diagnosticId);
        Assert.Equal(expectedUrl, helpLink);
    }
}
