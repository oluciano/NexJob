using Microsoft.AspNetCore.Http;

namespace NexJob.Dashboard;

/// <summary>The response headers the dashboard sends so a page cannot be framed, sniffed or leak its address.</summary>
internal static class DashboardSecurityHeaders
{
    /// <summary>Adds the headers the host has not set already.</summary>
    /// <param name="headers">The response headers.</param>
    internal static void Apply(IHeaderDictionary headers)
    {
        // Red stub (#595): the headers are added by the fix that follows this commit.
    }
}
