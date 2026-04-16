using FluentAssertions;
using Xunit;

namespace NexJob.Internal.Tests;

public sealed class JobTypeResolverTests
{
    [Fact]
    public void ResolveJobType_ValidType_ReturnsType()
    {
        var typeName = typeof(string).AssemblyQualifiedName;

        var result = JobTypeResolver.ResolveJobType(typeName!);

        result.Should().NotBeNull();
        result.Should().Be(typeof(string));
    }

    [Fact]
    public void ResolveJobType_UnknownType_ReturnsNull()
    {
        var result = JobTypeResolver.ResolveJobType("NonExistent.Type, FakeAssembly");

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveInputType_UnknownType_ReturnsNull()
    {
        var result = JobTypeResolver.ResolveInputType("NonExistent.Type, FakeAssembly");

        result.Should().BeNull();
    }
}
