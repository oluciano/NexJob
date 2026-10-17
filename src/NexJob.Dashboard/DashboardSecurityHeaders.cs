using Microsoft.AspNetCore.Http;

namespace NexJob.Dashboard;

/// <summary>The response headers the dashboard sends so a page cannot be framed, sniffed or leak its address.</summary>
internal static class DashboardSecurityHeaders
{
    // The pages use inline scripts and handlers, so script-src keeps 'unsafe-inline'. What the policy does here is
    // forbid framing, plugins, a changed <base> and a form that posts to another site, and keep every load on this
    // origin except the font stylesheet.
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; " +
        "form-action 'self'; frame-ancestors 'none'";

    /// <summary>Adds the headers the host has not set already.</summary>
    /// <param name="headers">The response headers.</param>
    internal static void Apply(IHeaderDictionary headers)
    {
        Add(headers, "X-Content-Type-Options", "nosniff");
        Add(headers, "X-Frame-Options", "DENY");
        Add(headers, "Referrer-Policy", "same-origin");
        Add(headers, "Content-Security-Policy", ContentSecurityPolicy);
    }

    private static void Add(IHeaderDictionary headers, string name, string value)
    {
        if (!headers.ContainsKey(name))
        {
            headers[name] = value;
        }
    }
}
