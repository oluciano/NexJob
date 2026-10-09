using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace NexJob.DocsTruth;

/// <summary>The default written in the documentation must be the real one.</summary>
internal static class DefaultsCheck
{
    private static readonly Regex DocumentedLine = new(
        @"^\s*options\.(?<name>\w+)\s*=.*?//\s*Default:\s*(?<doc>.+?)\s*$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(1));

    private static readonly Regex TimeSpanFactory = new(
        @"^TimeSpan\.From(?<unit>Milliseconds|Seconds|Minutes|Hours|Days)\((?<n>[\d.]+)\)$",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    /// <summary>Compares the documented <c>// Default:</c> comments with the values of a fresh instance.</summary>
    /// <param name="instance">A new instance of the options type.</param>
    /// <param name="wikiText">The text of the configuration page.</param>
    /// <returns>A drift finding for a wrong default, a review finding for one that cannot be compared.</returns>
    internal static IReadOnlyList<Finding> Run(object instance, string wikiText)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(wikiText);

        var findings = new List<Finding>();
        foreach (var groups in DocumentedLine.Matches(wikiText).Select(m => m.Groups))
        {
            var name = groups["name"].Value;
            var property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
            {
                continue;
            }

            var documented = groups["doc"].Value.Trim();
            var actual = property.GetValue(instance);
            var verdict = Compare(property.PropertyType, actual, documented);
            if (verdict is null)
            {
                findings.Add(new Finding("defaults", name, $"Documented default '{documented}' cannot be compared with the real value; check it by hand.", Severity.Review));
            }
            else if (!verdict.Value)
            {
                findings.Add(new Finding("defaults", name, $"Documented default is '{documented}' but the real default is '{Format(actual)}'."));
            }
        }

        return findings;
    }

    private static bool? Compare(Type type, object? actual, string documented)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (actual is null)
        {
            return string.Equals(documented, "null", StringComparison.OrdinalIgnoreCase) ? true : null;
        }

        if (target == typeof(bool))
        {
            return bool.TryParse(documented, out var flag) ? flag == (bool)actual : null;
        }

        if (target.IsEnum)
        {
            var member = documented[(documented.LastIndexOf('.') + 1)..];
            return Enum.TryParse(target, member, ignoreCase: true, out var parsed) ? Equals(parsed, actual) : null;
        }

        if (target == typeof(string))
        {
            return string.Equals(documented.Trim('"'), (string)actual, StringComparison.Ordinal);
        }

        if (target == typeof(TimeSpan))
        {
            return ParseTimeSpan(documented) is { } span ? span == (TimeSpan)actual : null;
        }

        if (IsNumeric(target))
        {
            return decimal.TryParse(documented, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number == Convert.ToDecimal(actual, CultureInfo.InvariantCulture)
                : null;
        }

        return null;
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(double) || type == typeof(float) || type == typeof(decimal);

    private static TimeSpan? ParseTimeSpan(string documented)
    {
        var factory = TimeSpanFactory.Match(documented);
        if (factory.Success)
        {
            var n = double.Parse(factory.Groups["n"].Value, CultureInfo.InvariantCulture);
            return factory.Groups["unit"].Value switch
            {
                "Milliseconds" => TimeSpan.FromMilliseconds(n),
                "Seconds" => TimeSpan.FromSeconds(n),
                "Minutes" => TimeSpan.FromMinutes(n),
                "Hours" => TimeSpan.FromHours(n),
                _ => TimeSpan.FromDays(n),
            };
        }

        return TimeSpan.TryParse(documented, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string Format(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
}
