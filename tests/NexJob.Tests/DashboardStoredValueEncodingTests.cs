using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NexJob.Tests;

/// <summary>Every value the dashboard reads from storage is encoded for its context on every page (#590).</summary>
public sealed class DashboardStoredValueEncodingTests
{
    // One value that is markup in HTML text, breaks out of a double-quoted attribute and out of a single-quoted JS string.
    private const string Hostile = "<i data-xss590>\"onx590=\"1\" '+x590()+'";

    // A quote after a backslash is escaped inside a JS string, so only an unescaped one breaks out of an attribute.
    private static readonly Regex[] RawFragments =
    [
        new(Regex.Escape("<i data-xss590>"), RegexOptions.None, TimeSpan.FromSeconds(1)),
        new("(?<!\\\\)\"onx590=", RegexOptions.None, TimeSpan.FromSeconds(1)),
        new(Regex.Escape("'+x590()+'"), RegexOptions.None, TimeSpan.FromSeconds(1)),
    ];

    private static string Encoded(string prefix) => prefix + Hostile;

    [Fact]
    public async Task EveryStoredField_IsEncodedOnEveryPage()
    {
        // N1: markup in every stored field never reaches any page raw, in text, attribute, URL or script context.
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = Encoded("Job"),
            InputType = Encoded("In"),
            InputJson = "{\"v\":\"" + Hostile.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"}",
            Queue = Encoded("q"),
            Status = JobStatus.Failed,
            IdempotencyKey = Encoded("k"),
            Tags = [Encoded("t")],
            CreatedAt = DateTimeOffset.UtcNow,
            ProcessingStartedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
            CompletedAt = DateTimeOffset.UtcNow,
            LastErrorMessage = Encoded("err"),
            LastErrorStackTrace = Encoded("at "),
            CheckpointJson = "{\"c\":\"" + Hostile.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"}",
            ProgressMessage = Encoded("p"),
            RecurringJobId = Encoded("rec"),
            ExecutionLogs = [new JobExecutionLog { Timestamp = DateTimeOffset.UtcNow, Level = "Information", Message = Encoded("log") }],
        };
        var recurring = new RecurringJobRecord
        {
            RecurringJobId = job.RecurringJobId,
            JobType = job.JobType,
            Cron = "* * * * *",
            Queue = job.Queue,
            CreatedAt = DateTimeOffset.UtcNow,
            LastExecutionError = Encoded("rerr"),
        };
        var server = new ServerRecord
        {
            Id = Encoded("srv"),
            WorkerCount = 1,
            Queues = [job.Queue],
            StartedAt = DateTimeOffset.UtcNow,
            HeartbeatAt = DateTimeOffset.UtcNow,
        };

        var routes = new[]
        {
            string.Empty, "jobs", $"jobs/{job.Id.Value}", "failed", "queues", "servers", "recurring",
            $"recurring/{Uri.EscapeDataString(recurring.RecurringJobId)}", "catalog", "settings", "listeners",
            $"jobs?search={Uri.EscapeDataString(Hostile)}&tag={Uri.EscapeDataString(Hostile)}&queue={Uri.EscapeDataString(Hostile)}",
            $"catalog?search={Uri.EscapeDataString(Hostile)}&queue={Uri.EscapeDataString(Hostile)}",
            $"failed?search={Uri.EscapeDataString(Hostile)}&queue={Uri.EscapeDataString(Hostile)}",
        };

        var pages = await DashboardTestHost.UseAsync(
            async s =>
            {
                await s.EnqueueAsync(job);
                await s.UpsertRecurringJobAsync(recurring);
                await s.RegisterServerAsync(server);
            },
            async (client, _) =>
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var route in routes)
                {
                    result[route] = await client.GetStringAsync($"/dashboard/{route}");
                }

                return result;
            },
            services => services.GetRequiredService<IListenerRegistry>().Register(
                new ListenerRegistration(Encoded("l"), Encoded("b"), Encoded("e"), Encoded("t"), Encoded("tag"), Encoded("cg"))));

        foreach (var (route, html) in pages)
        {
            foreach (var fragment in RawFragments)
            {
                fragment.IsMatch(html).Should().BeFalse($"route '{route}' must encode stored values");
            }
        }
    }

    [Fact]
    public void UnencodedValue_IsDetectedByTheCheck()
    {
        // N2: the raw fragments are what an unencoded interpolation would print, so a page that echoed them would fail N1.
        var echoed = $"<td>{Hostile}</td>";

        RawFragments.Should().OnlyContain(f => f.IsMatch(echoed));
    }

    [Fact]
    public async Task EmptyStorage_RendersEveryPage()
    {
        // N3: no stored data renders without error.
        var statuses = await DashboardTestHost.UseAsync<IReadOnlyList<int>>(
            null,
            async (client, _) =>
            {
                var codes = new List<int>();
                foreach (var route in new[] { string.Empty, "jobs", "failed", "queues", "servers", "recurring", "catalog", "settings", "listeners" })
                {
                    using var response = await client.GetAsync($"/dashboard/{route}");
                    codes.Add((int)response.StatusCode);
                }

                return codes;
            });

        statuses.Should().OnlyContain(c => c == 200);
    }
}
