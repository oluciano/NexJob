using FluentAssertions;
using NexJob.Dashboard;
using Xunit;

namespace NexJob.Tests;

/// <summary>Unit tests for <see cref="QueueScope"/> (#390).</summary>
public sealed class QueueScopeTests
{
    private readonly NexJobOptions _options = new() { QueuePrefix = "billing" };

    /// <summary>N1: <c>default</c> in a scope covers the prefixed default and the legacy default.</summary>
    /// <param name="entry">The configured entry.</param>
    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    public void Expand_Default_CoversPrefixedAndLegacy(string entry)
    {
        QueueScope.Expand([entry], _options).Should().Equal("billing.default", "default");
    }

    /// <summary>N1: other entries are kept, in order, next to the expanded default.</summary>
    [Fact]
    public void Expand_DefaultNextToNamedQueue_KeepsTheNamedQueue()
    {
        QueueScope.Expand(["default", "emails"], _options).Should().Equal("billing.default", "default", "emails");
    }

    /// <summary>N2: named and qualified entries match literally.</summary>
    /// <param name="entry">The configured entry.</param>
    [Theory]
    [InlineData("emails")]
    [InlineData("billing.default")]
    [InlineData("inventory.default")]
    public void Expand_NamedOrQualifiedEntry_IsLiteral(string entry)
    {
        QueueScope.Expand([entry], _options).Should().Equal(entry);
    }

    /// <summary>N2: an entry that is already in the expansion is not listed twice.</summary>
    [Fact]
    public void Expand_DefaultAndItsPrefixedName_AreNotDuplicated()
    {
        QueueScope.Expand(["default", "billing.default"], _options).Should().Equal("billing.default", "default");
    }

    /// <summary>N3: no scope means all queues, and an empty scope stays empty.</summary>
    [Fact]
    public void Expand_NullOrEmptyScope_IsReturnedAsIs()
    {
        QueueScope.Expand(null, _options).Should().BeNull();
        QueueScope.Expand([], _options).Should().BeEmpty();
    }

    /// <summary>N3: with no prefix at all the alias collapses to the plain default.</summary>
    [Fact]
    public void Expand_WithoutAnyPrefix_KeepsOnlyTheLegacyDefault()
    {
        var options = new NexJobOptions { EntryAssembly = null };

        QueueScope.Expand(["default"], options).Should().Equal("default");
    }

    /// <summary>N3: options are required.</summary>
    [Fact]
    public void Expand_NullOptions_Throws()
    {
        var act = () => QueueScope.Expand(["default"], null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>N1: the legacy default is shown while it holds jobs.</summary>
    [Fact]
    public void Resolve_LegacyDefaultHoldsJobs_IsShown()
    {
        QueueScope.Resolve(["default"], _options, [new QueueMetrics { Queue = "default", Enqueued = 2 }])
            .Should().Equal("billing.default", "default");
    }

    /// <summary>N2: an emptied legacy default is left out instead of staying as a zero row.</summary>
    [Fact]
    public void Resolve_LegacyDefaultEmpty_IsHidden()
    {
        QueueScope.Resolve(["default"], _options, [new QueueMetrics { Queue = "billing.default", Enqueued = 1 }])
            .Should().Equal("billing.default");
    }

    /// <summary>N3: with no prefix the plain default is the application's queue and is never hidden; no scope stays no scope.</summary>
    [Fact]
    public void Resolve_WithoutPrefixOrScope_DoesNotHideOrInvent()
    {
        var plain = new NexJobOptions { EntryAssembly = null };

        QueueScope.Resolve(["default"], plain, []).Should().Equal("default");
        QueueScope.Resolve(null, _options, []).Should().BeNull();
    }
}
