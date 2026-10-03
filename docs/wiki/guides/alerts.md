---
title: "Alerts: Know When Jobs Fail for Good"
sidebarTitle: "Alerts"
description: "How to get notified when a NexJob job exhausts its retries, expires, or a queue stops draining: the signals NexJob exposes and a tested recipe that posts to Slack."
---

NexJob does not send notifications itself. It exposes the **signals**, and you connect them to the tool your team already uses (Slack, Teams, Discord, e-mail, PagerDuty, Grafana). This keeps NexJob free of channel formats, retries and secrets, and it keeps the alert in your hands: what to say, to whom, and how often.

## The signals

| What you want to know | Best signal | Also available |
|---|---|---|
| A job **used all its attempts** (including a job whose node kept dying) | An [`IDeadLetterForwarder`](#recipe-post-to-slack) of your own, called once per such job | Dashboard **Failed / DLQ**; an `Error` log, `Job {JobId} exhausted all attempts - moving to dead-letter` (a job whose node died logs a `Warning` instead: `... stopped sending heartbeats and no attempts were left`) |
| A job **passed its deadline** before it started | Counter `nexjob.jobs.expired` | Dashboard **Failed / DLQ**, *Expired* tab |
| Failures are **rising** (retries included) | Counter `nexjob.jobs.failed` (one increment per failed *attempt*) | Dashboard overview |
| A queue **is not draining** | Gauge `nexjob.queue.depth` growing; gauge `nexjob.workers.active` at 0 | Dashboard **Queues**: `⚠️ NO WORKERS` badge |
| A queue's **circuit breaker opened** | Dashboard **Queues**: circuit state and **Reset Circuit** | There is no metric for the circuit state yet |

!!! note
    `nexjob.jobs.failed` counts **every failed attempt**, including those that will be retried, so it is not "the job is dead". For that, use a forwarder: it is called only when the attempts are over.

## Recipe: post to Slack

A dead-letter forwarder is called by the dispatcher for **every** job that exhausts its attempts, whatever its type, and a job that fails because its node crashed on the last attempt reaches it too (with an [`OrphanedJobException`](../concepts/retries-and-dead-letter.md#when-the-node-dies)). Register it once.

The forwarder runs **inside the worker slot that was running the job**, so it must not wait for the network. The recipe below only puts the message in a bounded queue and returns; a background service sends it.

```csharp
using System.Net.Http.Json;
using System.Threading.Channels;
using NexJob;

// 1. A bounded queue between the worker and the sender.
public sealed class SlackAlertQueue
{
    public Channel<string> Messages { get; } = Channel.CreateBounded<string>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropWrite });
}

// 2. Called by NexJob when a job has no attempts left. It only queues the message.
public sealed class SlackAlertForwarder : IDeadLetterForwarder
{
    private readonly SlackAlertQueue _queue;

    public SlackAlertForwarder(SlackAlertQueue queue) => _queue = queue;

    public bool AppliesTo(JobRecord failedJob) => true; // or: failedJob.Queue == "payments"

    public Task ForwardAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
    {
        var jobType = failedJob.JobType.Split(',')[0];
        var error = lastException.Message.Length > 300 ? lastException.Message[..300] + "..." : lastException.Message;

        // TryWrite never waits: when the queue is full the alert is dropped and the worker is not held.
        _queue.Messages.Writer.TryWrite(
            $":red_circle: *{jobType}* failed for good on queue `{failedJob.Queue}` after {failedJob.Attempts} attempts\n" +
            $"Job `{failedJob.Id.Value}`: {error}");

        return Task.CompletedTask;
    }
}

// 3. Sends the queued messages. A failure is logged and does not stop the next alerts.
public sealed class SlackAlertSender : BackgroundService
{
    private readonly SlackAlertQueue _queue;
    private readonly HttpClient _http;
    private readonly string _webhookUrl;
    private readonly ILogger<SlackAlertSender> _logger;

    public SlackAlertSender(SlackAlertQueue queue, HttpClient http, string webhookUrl, ILogger<SlackAlertSender> logger)
    {
        _queue = queue;
        _http = http;
        _webhookUrl = webhookUrl;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var text in _queue.Messages.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var response = await _http.PostAsJsonAsync(_webhookUrl, new { text }, stoppingToken);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "The Slack alert could not be delivered");
            }
        }
    }
}
```

Register them:

```csharp
builder.Services.AddSingleton<SlackAlertQueue>();
builder.Services.AddSingleton<IDeadLetterForwarder, SlackAlertForwarder>();
builder.Services.AddHostedService(sp => new SlackAlertSender(
    sp.GetRequiredService<SlackAlertQueue>(),
    new HttpClient { Timeout = TimeSpan.FromSeconds(5) },
    builder.Configuration["Alerts:SlackWebhookUrl"]!, // keep the URL in a secret store, it is a credential
    sp.GetRequiredService<ILogger<SlackAlertSender>>()));
```

To use Teams, Discord, e-mail or PagerDuty, keep the forwarder and the queue, and change only how the sender turns the text into a request. This recipe is covered by tests in the NexJob repository (`AlertRecipeTests`): one POST per exhausted job, a slow webhook does not hold the worker, a failing webhook does not stop the next alert, a burst larger than the queue is dropped without error, and a very long error message is truncated.

## What to watch out for

- **Do not wait for the network in the forwarder.** It runs in the worker slot, so a slow webhook would stop that worker from taking jobs. Queue and return, as above.
- **Never send the job input.** `JobRecord.InputJson` can hold personal or secret data. The recipe sends only the type, queue, attempts, job id and a truncated error message, and the error message itself can contain data: trim it, or leave it out, if your exceptions carry it.
- **Rate limiting is yours.** A failed dependency can dead-letter thousands of jobs in a minute. The bounded queue protects the process; to protect the channel, group the messages (for example "37 `ProcessPaymentJob` failed in the last minute") in the sender.
- **Each node alerts for the jobs it dead-lettered.** With several nodes, an outage produces alerts from each of them. Group in the receiving tool, or alert from a metric (below).
- **An exception in the forwarder is logged and swallowed.** It never stops the job pipeline, but it also means a broken forwarder fails silently. Watch the counter `nexjob.dead_letter.forward_failed`.
- **Put the webhook URL in a secret store.** Anyone with the URL can post to your channel.

## Alerting from metrics

If you already run Grafana or Alertmanager, you can alert without any code. Export the NexJob meter ([OpenTelemetry](../integrations/opentelemetry.md)) and write a rule on:

- `nexjob.dead_letter.forwarded`, tagged with `nexjob.forwarder`: it is incremented for every job a forwarder handled successfully, so with the forwarder above it counts the jobs that exhausted their attempts. An increase above zero is the alert.
- `nexjob.jobs.expired`: jobs that passed their deadline.
- `nexjob.queue.depth` growing while `nexjob.workers.active` is `0`: a queue nobody drains.

!!! note
    Your metrics exporter renames these (dots usually become underscores, and counters usually end in `_total`). Look the exact names up in your metrics explorer before writing the rule.

## See also

- [Retries & Dead Letter](../concepts/retries-and-dead-letter.md): handlers, forwarders and what an exhausted job looks like.
- [OpenTelemetry](../integrations/opentelemetry.md): the full list of metrics.
- [Dashboard](../integrations/dashboard.md): the Queues and Failed / DLQ views.
