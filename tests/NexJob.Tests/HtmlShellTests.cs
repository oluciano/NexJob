using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using NexJob.Dashboard;
using Xunit;

namespace NexJob.Tests;

public sealed class HtmlShellTests
{
    private static readonly Regex ThemeBlock = new(
        @"\[data-theme=""(?<theme>[a-z-]+)""\]\s*\{(?<body>[^}]*)\}",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    private static readonly Regex Token = new(
        @"--(?<name>[a-z-]+):\s*(?<value>#[0-9a-fA-F]{6})",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("blue-theme")]
    [InlineData("semi-dark")]
    [InlineData("bordered-theme")]
    public void HeaderLogo_HasReadableContrastAgainstHeader_InEveryTheme(string theme)
    {
        // N1 (Positive) + N2 (Negative, #562): the logo text meets WCAG 4.5:1 on the header, in semi-dark too.
        var tokens = TokensOf(theme);
        var logo = tokens.TryGetValue("header-text", out var headerText) ? headerText : tokens["text-primary"];

        Contrast(logo, tokens["header-bg"]).Should().BeGreaterThanOrEqualTo(4.5, $"theme {theme} logo");
    }

    [Fact]
    public void HeaderLogo_UnknownTheme_HasNoTokens()
    {
        // N3 (Invalid Input): a theme that does not exist yields no tokens instead of a stale match.
        TokensOf("no-such-theme").Should().BeEmpty();
    }

    private static Dictionary<string, string> TokensOf(string theme)
    {
        var html = HtmlShell.Wrap("t", "/dashboard", "overview", string.Empty);
        var block = ThemeBlock.Matches(html).FirstOrDefault(m => m.Groups["theme"].Value == theme);
        return block is null
            ? new Dictionary<string, string>()
            : Token.Matches(block.Groups["body"].Value).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);
    }

    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int i)
        {
            var c = int.Parse(hex.AsSpan(1 + (i * 2), 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(1)) + (0.0722 * Channel(2));
    }
}
