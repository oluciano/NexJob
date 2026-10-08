using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace NexJob.Tests;

/// <summary>
/// Hands out loopback TCP ports without ever giving the same port to two tests of the process.
/// Asking the OS for a free port and releasing it is not enough: tests run in parallel, and the
/// OS can return the same port to a second caller before the first host has bound it.
/// </summary>
internal static class TestPorts
{
    private static readonly ConcurrentDictionary<int, byte> HandedOut = new();

    /// <summary>Returns a port that is free now and was not returned before in this process.</summary>
    /// <returns>A loopback TCP port.</returns>
    public static int Next()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            if (HandedOut.TryAdd(port, 0))
            {
                return port;
            }
        }

        throw new InvalidOperationException("Could not find a TCP port that was not already handed out.");
    }
}
