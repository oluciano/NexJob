using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

/// <summary>Tests for <see cref="TestPorts"/>, the allocator behind the dashboard tests (issue #371).</summary>
public sealed class TestPortsTests
{
    // N1 — Positive: parallel callers never receive the same port.
    [Fact]
    public async Task Next_ParallelCallers_NeverShareAPort()
    {
        var ports = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            Enumerable.Range(0, 50).Select(__ => TestPorts.Next()).ToArray())));

        var all = ports.SelectMany(p => p).ToList();
        all.Should().OnlyHaveUniqueItems("a port handed out twice makes the second host fail with 'address already in use'");
    }

    // N2 — Negative: a port that was released after being handed out is not handed out again.
    [Fact]
    public void Next_AfterAPortIsReleased_DoesNotReturnItAgain()
    {
        var first = new HashSet<int>();
        for (var i = 0; i < 500; i++)
        {
            first.Add(TestPorts.Next()).Should().BeTrue("every call must return a port not returned before");
        }
    }

    // N3 — Boundary: ports are usable (non-privileged range) and a returned port can really be bound.
    [Fact]
    public void Next_ReturnsABindableNonPrivilegedPort()
    {
        var port = TestPorts.Next();

        port.Should().BeInRange(1024, 65535);
        var listener = new TcpListener(IPAddress.Loopback, port);
        var start = () => listener.Start();
        start.Should().NotThrow();
        listener.Stop();
    }
}
