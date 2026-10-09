using FluentAssertions;
using NexJob.Dashboard;
using Xunit;

namespace NexJob.Tests;

/// <summary>Unit tests for <see cref="OrphanQueueHint"/> (#391).</summary>
public sealed class OrphanQueueHintTests
{
    /// <summary>N1: an orphan shaped X.default suggests QueuePrefix = X and lists what the nodes poll.</summary>
    [Fact]
    public void For_PrefixedDefault_SuggestsThePrefixAndListsPolledQueues()
    {
        var hint = OrphanQueueHint.For("old.default", ["new.default", "default"]);

        hint.Should().Contain("QueuePrefix = old");
        hint.Should().Contain("new.default");
    }

    /// <summary>N1: a prefix with dots keeps all of them.</summary>
    [Fact]
    public void For_DottedPrefix_KeepsTheWholePrefix()
    {
        OrphanQueueHint.For("acme.billing.default", ["x.default"]).Should().Contain("QueuePrefix = acme.billing");
    }

    /// <summary>N2: a named queue gets the polled queues but no prefix suggestion.</summary>
    [Fact]
    public void For_NamedQueue_ListsPolledQueuesWithoutSuggestion()
    {
        var hint = OrphanQueueHint.For("emails", ["new.default"]);

        hint.Should().Contain("new.default");
        hint.Should().NotContain("QueuePrefix");
    }

    /// <summary>N2: without a live node there is nothing to compare with.</summary>
    [Fact]
    public void For_NoPolledQueues_ReturnsNull()
    {
        OrphanQueueHint.For("old.default", []).Should().BeNull();
    }

    /// <summary>N3: names that are not a prefix plus ".default" get no suggestion.</summary>
    /// <param name="orphan">The orphaned queue name.</param>
    [Theory]
    [InlineData("default")]
    [InlineData(".default")]
    [InlineData("x.")]
    [InlineData("x.defaults")]
    public void For_NotAPrefixedDefault_HasNoSuggestion(string orphan)
    {
        var hint = OrphanQueueHint.For(orphan, ["new.default"]);

        hint.Should().Contain("new.default");
        hint.Should().NotContain("QueuePrefix");
    }

    /// <summary>N3: a long list of polled queues stays bounded.</summary>
    [Fact]
    public void For_ManyPolledQueues_IsBounded()
    {
        var polled = Enumerable.Range(1, 12).Select(i => $"q{i}.default").ToList();

        var hint = OrphanQueueHint.For("old.default", polled);

        hint.Should().Contain("and 7 more");
        hint.Should().NotContain("q12.default");
    }

    /// <summary>N3: null arguments are rejected.</summary>
    [Fact]
    public void For_NullArguments_Throw()
    {
        var a = () => OrphanQueueHint.For(null!, ["x"]);
        var b = () => OrphanQueueHint.For("x", null!);

        a.Should().Throw<ArgumentNullException>();
        b.Should().Throw<ArgumentNullException>();
    }
}
