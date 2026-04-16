using FluentAssertions;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Unit tests for <see cref="JobTypeResolver"/> type resolution.
/// </summary>
public sealed class JobTypeResolverTests
{
    [Fact]
    public void ResolveJobType_ValidType_ReturnsType()
    {
        // Arrange
        var typeName = typeof(string).AssemblyQualifiedName;

        // Act
        var result = JobTypeResolver.ResolveJobType(typeName!);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(typeof(string));
    }

    [Fact]
    public void ResolveJobType_UnknownType_ReturnsNull()
    {
        // Arrange & Act
        var result = JobTypeResolver.ResolveJobType("NonExistent.Type, FakeAssembly");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveJobType_InvalidFormat_ReturnsNull()
    {
        // Arrange & Act
        var result = JobTypeResolver.ResolveJobType("InvalidFormat!@#$%");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveInputType_ValidType_ReturnsType()
    {
        // Arrange
        var typeName = typeof(int).AssemblyQualifiedName;

        // Act
        var result = JobTypeResolver.ResolveInputType(typeName!);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(typeof(int));
    }

    [Fact]
    public void ResolveInputType_UnknownType_ReturnsNull()
    {
        // Arrange & Act
        var result = JobTypeResolver.ResolveInputType("NonExistent.Type, FakeAssembly");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveInputType_InvalidFormat_ReturnsNull()
    {
        // Arrange & Act
        var result = JobTypeResolver.ResolveInputType("InvalidFormat!@#$%");

        // Assert
        result.Should().BeNull();
    }
}
