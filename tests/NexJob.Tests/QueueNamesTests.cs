using FluentAssertions;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>Unit tests for <see cref="QueueNames"/>.</summary>
public sealed class QueueNamesTests
{
    /// <summary>N1: the implicit default queue gets the prefix.</summary>
    /// <param name="queue">The requested queue.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    public void Resolve_ImplicitDefault_IsPrefixed(string? queue)
    {
        QueueNames.Resolve(queue, "billing").Should().Be("billing.default");
    }

    /// <summary>N1: the automatic prefix is the full entry assembly name, lowercase, never shortened.</summary>
    [Fact]
    public void DerivePrefix_UsesFullLowercaseAssemblyName()
    {
        QueueNames.DerivePrefix(typeof(QueueNamesTests).Assembly).Should().Be("nexjob.tests");
    }

    /// <summary>N2: queues named on purpose, and names already qualified, are left alone.</summary>
    /// <param name="queue">The requested queue.</param>
    [Theory]
    [InlineData("emails")]
    [InlineData("billing.default")]
    [InlineData("other.default")]
    public void Resolve_NamedOrQualifiedQueue_IsNotPrefixed(string queue)
    {
        QueueNames.Resolve(queue, "billing").Should().Be(queue);
    }

    /// <summary>N3: without a usable prefix the name is unchanged.</summary>
    /// <param name="prefix">The prefix.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NullOrBlankPrefix_KeepsDefault(string? prefix)
    {
        QueueNames.Resolve(null, prefix).Should().Be("default");
    }

    /// <summary>N3: no entry assembly means no derived prefix.</summary>
    [Fact]
    public void DerivePrefix_NullAssembly_ReturnsNull()
    {
        QueueNames.DerivePrefix(null).Should().BeNull();
    }
}
