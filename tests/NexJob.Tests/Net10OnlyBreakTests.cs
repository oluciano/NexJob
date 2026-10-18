using Xunit;

namespace NexJob.Tests;

public sealed class Net10OnlyBreakTests
{
    [Fact]
    public void FailsOnNet10Only() =>
        Assert.True(Environment.Version.Major < 10, "deliberate net10-only break (proof for #583 N1)");
}
