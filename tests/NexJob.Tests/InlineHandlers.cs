using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexJob.Tests;

/// <summary>Reads the inline <c>onclick</c> handlers of a page the way a browser does.</summary>
internal static class InlineHandlers
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    // A double-quoted attribute ends at the first double quote; the entities are decoded after that.
    private static readonly Regex OnClick = new(
        "\\sonclick=\"(?<v>[^\"]*)\"", RegexOptions.ExplicitCapture, Timeout);

    private static readonly Regex Confirm = new(
        "^return confirm\\('(?<m>(?:[^'\\\\]|\\\\.)*)'\\)$", RegexOptions.ExplicitCapture, Timeout);

    private static readonly Regex Copy = new(
        "^navigator\\.clipboard\\.writeText\\('(?<m>(?:[^'\\\\]|\\\\.)*)'\\);this\\.textContent='Copied!';setTimeout\\(\\(\\)=>this\\.textContent='[A-Za-z ]+',\\d+\\)$",
        RegexOptions.ExplicitCapture, Timeout);

    /// <summary>Every <c>onclick</c> value of the page, as the browser would hand it to the script engine.</summary>
    public static IReadOnlyList<string> All(string html) =>
        OnClick.Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups["v"].Value)).ToList();

    /// <summary>The handlers that start a <c>confirm</c> call (complete or not).</summary>
    public static IReadOnlyList<string> Confirms(string html) =>
        All(html).Where(h => h.StartsWith("return confirm(", StringComparison.Ordinal)).ToList();

    /// <summary>The message of a complete <c>confirm('...')</c> handler, or <see langword="null"/> when it is not one.</summary>
    public static string? ConfirmMessage(string handler) => Unescape(Confirm.Match(handler));

    /// <summary>The copied text of a complete clipboard handler, or <see langword="null"/> when it is not one.</summary>
    public static string? CopyValue(string handler) => Unescape(Copy.Match(handler));

    private static string? Unescape(Match match) =>
        match.Success ? JsonSerializer.Deserialize<string>($"\"{match.Groups["m"].Value}\"") : null;
}
