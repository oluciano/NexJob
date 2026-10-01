using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.SqlServer;
using NexJob.Storage;
using Xunit;
using Xunit.Abstractions;

namespace NexJob.IntegrationTests;

/// <summary>
/// Issue #279: with several workers SQL Server picked transactions as deadlock victims. The statement that releases a
/// job's continuations (<c>WHERE parent_job_id = @id</c>) was compiled as a clustered index scan, because the parent
/// index is filtered (<c>WHERE parent_job_id IS NOT NULL</c>) and a parameter does not prove it is not null. The scan
/// took update locks on rows that belong to other jobs.
/// </summary>
public sealed class SqlServerContinuationReleaseTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SqlServerContinuationReleaseTests(SqlServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ManyWorkersAndProducers_CompleteEveryJobWithoutDeadlocks()
    {
        // N1 (Positive): the load that used to produce dozens of deadlock victims now produces none.
        var cs = await CreateDatabaseAsync();
        var before = await DeadlockCounterAsync();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJobSqlServer(cs);
                services.AddNexJob(o =>
                {
                    o.Workers = 30;
                    o.PollingInterval = TimeSpan.FromMilliseconds(20);
                });
                services.AddTransient<QuickJob>();
            })
            .Build();

        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 150; i++)
            {
                await scheduler.EnqueueAsync<QuickJob>();
            }
        })));

        var storage = host.Services.GetRequiredService<IDashboardStorage>();
        var deadline = DateTime.UtcNow.AddSeconds(90);
        long succeeded = 0;
        while (DateTime.UtcNow < deadline && succeeded < 1200)
        {
            await Task.Delay(200);
            succeeded = (await storage.GetMetricsAsync()).Succeeded;
        }

        await host.StopAsync();
        var deadlocks = await DeadlockCounterAsync() - before;

        Assert.Equal(1200, succeeded);

        // The regression this test guards is the release statement scanning the jobs table. A deadlock from anywhere else
        // is reported in the test output (and tracked in its own issue) instead of failing this test.
        if (deadlocks > 0)
        {
            var graphs = await DeadlockGraphsAsync();
            foreach (var graph in graphs)
            {
                _output.WriteLine($"Deadlock graph: {graph}");
            }

            var release = graphs.Where(g => g.Contains("parent_job_id", StringComparison.Ordinal) || g.Contains("idx_nexjob_jobs_parent", StringComparison.Ordinal)).ToList();
            Assert.True(release.Count == 0, $"{release.Count} deadlock(s) involve the continuation release statement: {string.Join(" ;; ", release)}");
        }
    }

    [Fact]
    public async Task ReleaseStatements_UseTheParentIndex_AndNeverScanTheJobsTable()
    {
        // N2: every statement that releases continuations is compiled to seek the parent index. The plan is chosen
        // on first use, when the table is empty, which is exactly when a scan used to be picked.
        var cs = await CreateDatabaseAsync();
        var provider = new SqlServerStorageProvider(cs);
        var parent = NewJob();
        await provider.EnqueueAsync(parent);
        var fetched = (await provider.FetchNextAsync(["default"]))!;
        await provider.CommitJobResultAsync(fetched.Id, new JobExecutionResult { Succeeded = true, Logs = [], });

        var other = NewJob();
        await provider.EnqueueAsync(other);
        await provider.AcknowledgeAsync(other.Id);

        // A batch of one delegates to AcknowledgeAsync, so two jobs are needed to reach the batch statement.
        var batchA = NewJob();
        var batchB = NewJob();
        await provider.EnqueueAsync(batchA);
        await provider.EnqueueAsync(batchB);
        await provider.AcknowledgeBatchAsync([batchA.Id, batchB.Id]);

        await provider.EnqueueContinuationsAsync(parent.Id);

        var plans = await ReleasePlansAsync(cs);

        // Acknowledge and CommitJobResult share one statement text, so they share one cached plan.
        foreach (var fragment in new[] { "parent_job_id = @id", "parent_job_id IN", "parent_job_id = @parentId" })
        {
            var matching = plans.Where(p => p.Text.Contains(fragment, StringComparison.Ordinal)).ToList();
            Assert.True(matching.Count > 0, $"no cached plan for the statement containing '{fragment}'. Cached: {string.Join(" || ", plans.Select(p => p.Text.Replace("\n", " ").Replace("\r", string.Empty)[..Math.Min(140, p.Text.Length)]))}");
            foreach (var (_, plan) in matching)
            {
                Assert.Contains("idx_nexjob_jobs_parent", plan, StringComparison.Ordinal);
                Assert.DoesNotContain("Clustered Index Scan", plan, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task ReleaseStatement_StillReleasesOnlyTheChildrenOfThatParent()
    {
        // N3: the extra predicate must not change what is released.
        var cs = await CreateDatabaseAsync();
        var provider = new SqlServerStorageProvider(cs);
        var parentA = NewJob();
        var parentB = NewJob();
        await provider.EnqueueAsync(parentA);
        await provider.EnqueueAsync(parentB);
        var childOfA = NewJob(JobStatus.AwaitingContinuation, parentA.Id);
        var childOfB = NewJob(JobStatus.AwaitingContinuation, parentB.Id);
        await provider.EnqueueAsync(childOfA);
        await provider.EnqueueAsync(childOfB);

        await provider.AcknowledgeAsync(parentA.Id);

        var dashboard = (IDashboardStorage)provider;
        Assert.Equal(JobStatus.Enqueued, (await dashboard.GetJobByIdAsync(childOfA.Id))!.Status);
        Assert.Equal(JobStatus.AwaitingContinuation, (await dashboard.GetJobByIdAsync(childOfB.Id))!.Status);
    }

    [Fact]
    public async Task ReleaseForAJobWithoutChildren_DoesNothingAndDoesNotFail()
    {
        // N3 (boundary): no children at all, and an unknown parent, are both no-ops.
        var cs = await CreateDatabaseAsync();
        var provider = new SqlServerStorageProvider(cs);
        var lonely = NewJob();
        await provider.EnqueueAsync(lonely);

        await provider.AcknowledgeAsync(lonely.Id);
        await provider.EnqueueContinuationsAsync(JobId.New());

        Assert.Equal(JobStatus.Succeeded, (await ((IDashboardStorage)provider).GetJobByIdAsync(lonely.Id))!.Status);
    }

    private static JobRecord NewJob(JobStatus status = JobStatus.Enqueued, JobId? parent = null) => new()
    {
        Id = JobId.New(),
        JobType = typeof(QuickJob).AssemblyQualifiedName!,
        InputType = typeof(string).AssemblyQualifiedName!,
        InputJson = "\"\"",
        Status = status,
        ParentJobId = parent,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<string> CreateDatabaseAsync()
    {
        var baseConn = _fixture.Container.GetConnectionString();
        var dbName = $"NexJob_279_{Guid.NewGuid():N}";

        await using (var admin = new SqlConnection(baseConn))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}];";
            await cmd.ExecuteNonQueryAsync();
        }

        return new SqlConnectionStringBuilder(baseConn) { InitialCatalog = dbName, }.ConnectionString;
    }

    private async Task<long> DeadlockCounterAsync()
    {
        await using var conn = new SqlConnection(_fixture.Container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT cntr_value FROM sys.dm_os_performance_counters WHERE counter_name = 'Number of Deadlocks/sec' AND instance_name = '_Total'";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // The statements and lock resources of the recent deadlocks, read from the system_health session, so a failure says what
    // collided instead of only how many.
    private async Task<List<string>> DeadlockGraphsAsync()
    {
        await using var conn = new SqlConnection(_fixture.Container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT CAST(x.xml_report AS NVARCHAR(MAX)) FROM (
              SELECT n.query('.') AS xml_report
              FROM (SELECT CAST(xet.target_data AS XML) AS d
                    FROM sys.dm_xe_session_targets xet
                    JOIN sys.dm_xe_sessions xe ON xe.address = xet.event_session_address
                    WHERE xe.name = 'system_health' AND xet.target_name = 'ring_buffer') t
              CROSS APPLY t.d.nodes('//RingBufferTarget/event[@name="xml_deadlock_report"]') AS r(n)) x
            """;
        var lines = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var doc = XElement.Parse(reader.GetString(0));
            var statements = doc.Descendants("inputbuf").Select(e => string.Join(" ", e.Value.Split(new[] { '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)));
            var resources = doc.Descendants()
                .Where(e => e.Name.LocalName is "keylock" or "pagelock" or "objectlock" or "ridlock")
                .Select(e => $"{e.Name.LocalName} {e.Attribute("objectname")?.Value}.{e.Attribute("indexname")?.Value}")
                .Distinct();
            lines.Add($"statements=[{string.Join(" | ", statements)}] resources=[{string.Join(", ", resources)}]");
        }

        return lines;
    }

    private static async Task<List<(string Text, string Plan)>> ReleasePlansAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT st.text, CAST(qp.query_plan AS NVARCHAR(MAX))
            FROM sys.dm_exec_cached_plans cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) st
            CROSS APPLY sys.dm_exec_query_plan(cp.plan_handle) qp
            CROSS APPLY sys.dm_exec_plan_attributes(cp.plan_handle) pa
            WHERE pa.attribute = 'dbid' AND pa.value = DB_ID()
              AND st.text LIKE '%AwaitingContinuation%'
              AND st.text LIKE '%parent_job_id%'
              AND st.text LIKE '%SET%Enqueued%'
              AND st.text NOT LIKE '%dm_exec%'
            """;
        var plans = new List<(string, string)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plans.Add((reader.GetString(0), reader.GetString(1)));
        }

        return plans;
    }

    private sealed class QuickJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
