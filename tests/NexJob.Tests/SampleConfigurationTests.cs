using System.Text.Json;
using FluentAssertions;
using NexJob.Configuration;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Guards the sample projects against configuration drift: keys that <see cref="NexJobSettings"/> silently ignores,
/// and recurring jobs aimed at queues the dispatcher never polls (issue #177).
/// </summary>
public sealed class SampleConfigurationTests
{
    // Keys under "NexJob" that are read by other code (broker triggers), not by NexJobSettings.
    private static readonly string[] ExtraSectionKeys = ["Triggers"];

    [Fact]
    public void SampleAppSettings_NexJobSection_OnlyUsesKeysTheSettingsClassReads()
    {
        // N1 (Positive): every key in every sample's NexJob section is a real setting.
        var known = typeof(NexJobSettings).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (sample, section) in LoadSamples())
        {
            FindUnknownKeys(section, known).Should().BeEmpty($"sample '{sample}' must not configure keys NexJobSettings ignores");
        }
    }

    [Fact]
    public void SampleAppSettings_RecurringJobs_TargetPolledQueues()
    {
        // N1 (Positive): a recurring job in a queue that is not polled would be enqueued and never run.
        foreach (var (sample, section) in LoadSamples())
        {
            var polled = ReadPolledQueues(section);
            foreach (var queue in ReadRecurringQueues(section))
            {
                polled.Should().Contain(queue, $"sample '{sample}' has a recurring job on queue '{queue}' that no worker polls");
            }
        }
    }

    [Fact]
    public void FindUnknownKeys_BogusKey_IsReported()
    {
        // N2 (Negative): the guard really fails for a key that does not exist.
        using var doc = JsonDocument.Parse("""{ "WorkerCount": 4, "Workers": 2 }""");

        FindUnknownKeys(doc.RootElement, ["Workers"]).Should().Equal("WorkerCount");
    }

    [Fact]
    public void ReadPolledQueues_NoQueuesConfigured_DefaultsToDefaultQueueOnly()
    {
        // N3 (Invalid Input / boundary): an empty section or empty array falls back to the "default" queue.
        using var empty = JsonDocument.Parse("{}");
        using var emptyArray = JsonDocument.Parse("""{ "Queues": [] }""");

        ReadPolledQueues(empty.RootElement).Should().Equal("default");
        ReadPolledQueues(emptyArray.RootElement).Should().Equal("default");
    }

    private static List<string> FindUnknownKeys(JsonElement section, IEnumerable<string> known)
    {
        var allowed = new HashSet<string>(known.Concat(ExtraSectionKeys), StringComparer.OrdinalIgnoreCase);
        return section.EnumerateObject().Select(p => p.Name).Where(n => !allowed.Contains(n)).ToList();
    }

    private static List<string> ReadPolledQueues(JsonElement section)
    {
        if (section.TryGetProperty("Queues", out var queues) && queues.GetArrayLength() > 0)
        {
            return queues.EnumerateArray().Select(q => q.GetString()!).ToList();
        }

        return ["default"];
    }

    private static List<string> ReadRecurringQueues(JsonElement section)
    {
        if (!section.TryGetProperty("RecurringJobs", out var jobs))
        {
            return [];
        }

        return jobs.EnumerateArray()
            .Select(j => j.TryGetProperty("Queue", out var q) ? q.GetString() ?? "default" : "default")
            .ToList();
    }

    private static List<(string Sample, JsonElement Section)> LoadSamples()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "NexJob.sln")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository root (NexJob.sln) must be reachable from the test output");

        var result = new List<(string, JsonElement)>();
        foreach (var file in Directory.GetFiles(Path.Combine(root!.FullName, "samples"), "appsettings.json", SearchOption.AllDirectories))
        {
            // Build output keeps stale copies of appsettings.json; only the sources count.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty(NexJobSettings.SectionName, out var section))
            {
                result.Add((Path.GetFileName(Path.GetDirectoryName(file))!, section.Clone()));
            }
        }

        result.Should().NotBeEmpty("at least one sample configures the NexJob section");
        return result;
    }
}
