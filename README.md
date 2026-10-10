<div align="center">

# NexJob

**Background jobs for .NET that survive the worst day.**<br/>
A node that dies, a deploy in the middle of a job, an upgrade from the previous version.

📖 **Documentation: [oluciano.github.io/NexJob](https://oluciano.github.io/NexJob/)** &nbsp;|&nbsp; 🎮 **Live Demo: [nexjob-playground.fly.dev](https://nexjob-playground.fly.dev/)**

[![NuGet](https://img.shields.io/nuget/v/NexJob.svg?style=flat-square&color=512bd4&label=nuget)](https://www.nuget.org/packages/NexJob)
[![NuGet Downloads](https://img.shields.io/nuget/dt/NexJob?style=flat-square&color=512bd4)](https://www.nuget.org/packages/NexJob)
[![Build](https://img.shields.io/github/actions/workflow/status/oluciano/NexJob/ci.yml?style=flat-square)](https://github.com/oluciano/NexJob/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg?style=flat-square)](LICENSE)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/oluciano/NexJob/badge)](https://scorecard.dev/viewer/?uri=github.com/oluciano/NexJob)
[![OpenSSF Best Practices](https://www.bestpractices.dev/projects/15362/badge)](https://www.bestpractices.dev/projects/15362)
[![CodeQL](https://github.com/oluciano/NexJob/actions/workflows/codeql.yml/badge.svg?branch=develop)](https://github.com/oluciano/NexJob/security/code-scanning)

<sub>The Scorecard, Best Practices and CodeQL badges rate the repository's security and engineering practices (pinned actions, least-privilege tokens, dependency updates, documented process, static analysis). The Best Practices badge is self-declared; the CodeQL badge says the analysis ran on the default branch, and the open alerts are in the [Security tab](https://github.com/oluciano/NexJob/security/code-scanning). None of them says anything about throughput or behaviour under failure.</sub>

<br/>

[![A job fails three times and reaches the dead-letter queue](docs/assets/dead-letter.gif)](https://nexjob-playground.fly.dev/)

<sub>A job fails three times and lands in the dead-letter queue with its error, ready to requeue. The two others succeed.</sub>

</div>

---

## What is NexJob?

A background job library for .NET 8 with five storage providers (PostgreSQL, SQL Server, Redis, MongoDB, in-memory), retries, dead-letter handling, deadlines and a built-in dashboard.

It is built for jobs that **must run, fail safely and leave a trace**: every failure has a documented cost (see [Delivery guarantees](https://oluciano.github.io/NexJob/concepts/delivery-guarantees/)), and the behaviour is tested against real databases, not only mocks.

---

## Quick Start

```bash
dotnet add package NexJob
```

```csharp
// Program.cs: register NexJob (in-memory storage by default) and scan for jobs
builder.Services.AddNexJob();
builder.Services.AddNexJobJobs(typeof(Program).Assembly);
```

```csharp
// A job
public sealed class SendInvoiceJob : IJob<SendInvoiceInput>
{
    private readonly IEmailService _email;

    public SendInvoiceJob(IEmailService email) => _email = email;

    public async Task ExecuteAsync(SendInvoiceInput input, CancellationToken ct)
        => await _email.SendAsync(input.Email, "Your invoice", ct);
}

public sealed record SendInvoiceInput(string Email);
```

```csharp
// Enqueue it from anywhere. It expires if it does not start within 5 minutes.
var scheduler = app.Services.GetRequiredService<IScheduler>();
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new SendInvoiceInput("customer@example.com"),
    deadlineAfter: TimeSpan.FromMinutes(5));
```

To watch it run, add the dashboard (`dotnet add package NexJob.Dashboard`) and `app.UseNexJobDashboard("/dashboard");`, then open `/dashboard`. See [Dashboard](#dashboard) for the Worker Service variant, or try the [live demo](https://nexjob-playground.fly.dev/) first.

To use a database instead of memory, see the [storage providers](https://oluciano.github.io/NexJob/storage/overview/).

---

## Why you can trust it

A job library is judged on the worst day. This is what the test suite proves ([the full page](https://oluciano.github.io/NexJob/reference/how-we-test/)):

- **The same tests on every provider.** One contract suite runs on InMemory, PostgreSQL, SQL Server, Redis and MongoDB.
- **Several nodes, one database.** Reliability scenarios run many workers and nodes on the four databases: each job runs once, no job is lost under concurrent enqueue, and a job left behind by a dead node is recovered and runs once.
- **A killed process.** On SQL Server a node runs in its own process and is killed in the middle of a job.
- **Upgrades.** A storage filled by the previous version opens with the new one, with its history and orphans intact.
- **An honest list of gaps.** The process kill is tested on SQL Server only, and wake-up latency is not asserted. The list is in the page above.

---

## What you get

| You need | NexJob gives you |
|---|---|
| **Jobs that retry and then stop** | Global and per-job [retries](https://oluciano.github.io/NexJob/concepts/retries-and-dead-letter/), `maxAttempts` per enqueue, [dead-letter handlers and forwarders](https://oluciano.github.io/NexJob/concepts/retries-and-dead-letter/), failures that skip retries, [`[ExecutionTimeout]`](https://oluciano.github.io/NexJob/concepts/retries-and-dead-letter/#execution-timeout) |
| **No zombie jobs** | [Deadlines](https://oluciano.github.io/NexJob/concepts/scheduling/#deadlines), [crash recovery](https://oluciano.github.io/NexJob/concepts/delivery-guarantees/) by heartbeat, `DuplicatePolicy` [idempotency](https://oluciano.github.io/NexJob/concepts/idempotency/) |
| **Protect a dependency** | [Queue circuit breaker](https://oluciano.github.io/NexJob/guides/circuit-breaker/), [`[Throttle]`](https://oluciano.github.io/NexJob/guides/throttling/) per resource (cluster-wide with Redis), [execution windows](https://oluciano.github.io/NexJob/guides/execution-windows/) |
| **Several services, one database** | [Queue isolation by default](https://oluciano.github.io/NexJob/concepts/queues/#the-default-queue) (`{prefix}.default` per application), [multi-service guide](https://oluciano.github.io/NexJob/guides/multi-service/) |
| **Schedule** | [Recurring jobs](https://oluciano.github.io/NexJob/concepts/recurring-jobs/) from code or `appsettings.json`, with time zones; delayed jobs; continuations; priority |
| **See and control it** | [Dashboard](https://oluciano.github.io/NexJob/integrations/dashboard/), [runtime control](https://oluciano.github.io/NexJob/guides/runtime-control/) (pause, requeue, delete), [OpenTelemetry](https://oluciano.github.io/NexJob/integrations/opentelemetry/), health checks, [alerts](https://oluciano.github.io/NexJob/guides/alerts/) |
| **Feed it from brokers** | [Triggers](https://oluciano.github.io/NexJob/integrations/triggers/) for Kafka, RabbitMQ, AWS SQS, Azure Service Bus, Google Pub/Sub and Salesforce, and a resilient outbox for RabbitMQ and Kafka |

---

## Storage Providers

| Provider | Package | Status |
|---|---|---|
| InMemory | `NexJob` (core) | Production ready |
| PostgreSQL | `NexJob.Postgres` | Production ready |
| SQL Server | `NexJob.SqlServer` | Production ready |
| Redis | `NexJob.Redis` | Production ready |
| MongoDB | `NexJob.MongoDB` | Production ready |

All providers implement `IRuntimeSettingsStore` — runtime configuration persists across restarts.

---

## Packages

All packages share one version number.

| Package | NuGet | Description |
|---|---|---|
| `NexJob.Dashboard` | [NuGet](https://www.nuget.org/packages/NexJob.Dashboard) | Embedded ASP.NET Core dashboard middleware |
| `NexJob.Dashboard.Standalone` | [NuGet](https://www.nuget.org/packages/NexJob.Dashboard.Standalone) | Embedded HTTP dashboard server for Worker Services |
| `NexJob.OpenTelemetry` | [NuGet](https://www.nuget.org/packages/NexJob.OpenTelemetry) | OTel SDK instrumentation |
| `NexJob.Trigger.AzureServiceBus` | [NuGet](https://www.nuget.org/packages/NexJob.Trigger.AzureServiceBus) | Azure Service Bus trigger |
| `NexJob.Trigger.AwsSqs` | [NuGet](https://www.nuget.org/packages/NexJob.Trigger.AwsSqs) | AWS SQS trigger |
| `NexJob.RabbitMQ` | [NuGet](https://www.nuget.org/packages/NexJob.RabbitMQ) | RabbitMQ trigger & resilient outbox producer |
| `NexJob.Kafka` | [NuGet](https://www.nuget.org/packages/NexJob.Kafka) | Apache Kafka trigger & resilient outbox producer |
| `NexJob.Trigger.GooglePubSub` | [NuGet](https://www.nuget.org/packages/NexJob.Trigger.GooglePubSub) | Google Cloud Pub/Sub trigger |
| `NexJob.Trigger.Salesforce` | [NuGet](https://www.nuget.org/packages/NexJob.Trigger.Salesforce) | Salesforce Pub/Sub API trigger (gRPC & Avro) |
| `NexJob.Trigger.SalesforceStreaming` | [NuGet](https://www.nuget.org/packages/NexJob.Trigger.SalesforceStreaming) | Salesforce Streaming API trigger (CometD & Bayeux) |

---

## Dashboard

The dashboard provides real-time operational visibility into your background jobs, worker nodes, and message broker listeners — with zero external dependencies.

🎮 **Try it live:** [nexjob-playground.fly.dev](https://nexjob-playground.fly.dev/) (interactive scenario simulator: burst jobs, outages, retries, and dead-letters).

[![NexJob Live Broker Listeners & Event Triggers](docs/assets/dashboard-listeners.png)](docs/assets/dashboard-listeners.png)

- Live cluster status, `Ctrl + K` job search, and a topology map from triggers to queues to worker nodes.
- Event listeners page for the connected brokers, and a job catalog with error rates and deep links to history.
- Streaming logs and progress bars (SSE), and five themes (`DefaultTheme = "semi-dark"`, `"blue-theme"`, `"dark"`, `"light"`, `"bordered"`).
- Opt-in scenario simulator (`EnablePlayground = true`, off by default) to try burst loads and failures without writing scripts.

### ASP.NET Core Web App

Install package:
```bash
dotnet add package NexJob.Dashboard
```

Configure in `Program.cs`:
```csharp
using NexJob;
using NexJob.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Register NexJob (InMemory storage by default, or your chosen provider)
builder.Services.AddNexJob();

var app = builder.Build();

// Mount dashboard at /dashboard
app.UseNexJobDashboard("/dashboard");

app.Run();
```

### Worker Service (Standalone)

For Worker Services or console applications without ASP.NET Core:
```bash
dotnet add package NexJob.Dashboard.Standalone
```

Configure in `Program.cs`:
```csharp
using NexJob.Dashboard.Standalone;

builder.Services.AddNexJob();

// Starts an embedded HTTP server at http://localhost:5005/dashboard
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Port = 5005;
    // options.DefaultTheme = "semi-dark";
    // options.EnablePlayground = true; // enable scenario drawer in dev/staging
});
```

---

## Documentation

Complete documentation is on the [Documentation Site](https://oluciano.github.io/NexJob/). Key pages:

- **[Mental Model](https://oluciano.github.io/NexJob/mental-model/)** — how NexJob works, read this first
- **[Quickstart](https://oluciano.github.io/NexJob/quickstart/)** — run your first job in 2 minutes
- **[Delivery Guarantees](https://oluciano.github.io/NexJob/concepts/delivery-guarantees/)** — what each failure costs a job: crashes, shutdown, throttling, pauses, deadlines
- **[Circuit Breaker](https://oluciano.github.io/NexJob/guides/circuit-breaker/)** and **[Throttling](https://oluciano.github.io/NexJob/guides/throttling/)** — protect a queue from a failing dependency, and limit concurrency
- **[Alerts](https://oluciano.github.io/NexJob/guides/alerts/)** — get notified when a job fails for good
- **[Best Practices](https://oluciano.github.io/NexJob/guides/best-practices/)** — production and Kubernetes guidelines
- **[Common Scenarios](https://oluciano.github.io/NexJob/guides/common-scenarios/)** — real-world use cases with code
- **[How We Test](https://oluciano.github.io/NexJob/reference/how-we-test/)** — what the test suite proves, and what it does not
- **[Troubleshooting](https://oluciano.github.io/NexJob/reference/troubleshooting/)** — debug common issues

---

## Samples

Runnable examples for the web, worker, storage and broker scenarios are in [`samples/`](samples/), with the full catalog and a Docker Compose stack (PostgreSQL, Redis, RabbitMQ, Kafka) in [`samples/README.md`](samples/README.md).

---

## Benchmarks

Hangfire is the usual reference for .NET background jobs, so it is the comparison here. NexJob is not a drop-in replacement for it: if you need calendar-based scheduling, distributed execution across untrusted networks or a large plugin ecosystem, Hangfire is the better choice.

The figures below measure the cost of **enqueueing** one job, not processing throughput. Measured per individual enqueue operation on .NET 8 (BenchmarkDotNet v0.14, RyuJIT AVX2, in-memory storage baseline):

| Metric | NexJob | Hangfire | Comparison |
|---|---|---|---|
| Latency (Mean) | **13.35 µs** | 35.95 µs | **2.7× faster** |
| Memory (Allocated) | **2.10 KB** | 11.20 KB | **81% less memory** |
| GC Gen0 (per 1k ops) | **0.06** | 0.85 | **14× fewer Gen0 collections** |
| GC Gen1 (per 1k ops) | **0.00** | 0.18 | **Zero Gen1 collections** |

NexJob is **2.7× faster**, allocates **81% less memory**, and produces zero Gen1 garbage collections during enqueue bursts. Hangfire incurs additional CPU and allocation overhead due to runtime LINQ expression tree parsing and reflection.

Benchmarks can be parameterized by payload size (`PayloadBytes: 0, 1024, 10240`) and concurrency levels (`ConcurrencyLevel: 10, 50`) in [`benchmarks/NexJob.Benchmarks`](benchmarks/NexJob.Benchmarks).

---

## Ecosystem & Companion Projects

- **[qKafka](https://github.com/oluciano/QKafka)**: If you need event-driven choreographies, distributed sagas with compensating transactions, and native Kafka stream state machines, check out qKafka. NexJob focuses on background job scheduling and resilient outbox dispatch, seamlessly bridging events to and from Kafka.

---

## Contributing

Found a bug or have an idea? [Open an issue](https://github.com/oluciano/NexJob/issues) after searching the existing ones. To send code, read [CONTRIBUTING.md](CONTRIBUTING.md): branch from `develop`, open the pull request against `develop`, build with zero warnings, and cover the change with tests (positive, negative and invalid input). Vulnerabilities go through the private channel in [SECURITY.md](SECURITY.md), not a public issue.

## Recent releases

```
v6.1.0  ✅ The PostgreSQL and SQL Server fetch uses the index (no longer reads the backlog); queue names match exactly;
           Salesforce `AuthEndpoint` and `InstanceUrl` must be https; supply-chain hardening (Scorecard, CodeQL)
v6.0.0  ✅ The default queue is isolated per application (`{prefix}.default`) with a drain of the legacy `default`;
           orphaned-queue hint and recurring id collision warning in the dashboard; `Workers = 0` and
           `DisableWorkers` really run a host without job execution
v5.10.0 ✅ `EnvironmentName` badge on the dashboard, a dashboard layout that fits phones
v5.9.0  ✅ `[ExecutionTimeout]`, per-job `maxAttempts`, `IgnoreRetryAttemptExceptions`, `DaysOfWeek` on execution windows
v5.8.0  ✅ `deadlineAfter` enforced on every database provider, `IDeadLetterForwarder`, new guides
```

The full history is in the [CHANGELOG](CHANGELOG.md). Security issues: see [SECURITY.md](SECURITY.md).

---

<div align="center">
<br/>

*Built with obsession over developer experience and production reliability.*

<br/>

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE) &nbsp;&nbsp; © 2025 [Luciano Azevedo](https://github.com/oluciano)

<img referrerpolicy="no-referrer-when-downgrade" src="https://static.scarf.sh/a.png?x-pxid=088bbff5-783b-4a01-aab6-c4f00637fe56" />

</div>
