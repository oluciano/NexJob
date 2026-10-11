---
title: "NexJob Retries: Policy, Backoff, Timeouts and Attempt Limits"
sidebarTitle: "Retries"
description: "Configure global and per-job retry policies with exponential backoff in NexJob, cap how long a job may run, set an attempt limit per enqueue, and skip retries for failures that cannot be fixed."
---

NexJob automatically retries failed jobs using a configurable delay strategy. You control two layers: a global policy that applies to every job, and per-job overrides for jobs that need different behavior. When a job runs out of attempts it is marked `Failed` and [dead-letter handling](dead-letter.md) takes over.

This page is about failures the job reports. For what a crash, a shutdown, a saturated throttle or a passed deadline does to a job, see [Delivery Guarantees](delivery-guarantees.md).

## Global retry policy

Set the default maximum number of attempts for all jobs in `AddNexJob`:

```csharp
builder.Services.AddNexJob(options =>
{
    options.MaxAttempts = 5; // 1 initial run + 4 retries
});
```

The default is **10 attempts** if you do not configure `MaxAttempts`.

### Default delay between attempts

Unless a job sets its own `InitialDelay` with `[Retry]`, NexJob computes the wait time using `options.RetryDelayFactory`. The built-in factory uses a polynomial curve with jitter: `attempt⁴ + 15` seconds plus a random 0–29 seconds multiplied by `(attempt + 1)`. This grows quickly — roughly 16 to 74 seconds after the first failure and 4.5 to 7 minutes after the fourth.

## Per-job retry override

Apply the `[Retry]` attribute to a job class to override the global policy for that job only. When you set `InitialDelay`, NexJob uses exponential backoff with ±10% jitter instead of the global factory.

```csharp
[Retry(5, InitialDelay = "00:00:30", Multiplier = 2.0, MaxDelay = "01:00:00")]
public sealed class ProcessPaymentJob : IJob<PaymentInput>
{
    public async Task ExecuteAsync(PaymentInput input, CancellationToken ct)
    {
        // 5 total attempts: first run + 4 retries
        // Delays (with ±10% jitter): ~30s → ~60s → ~120s → ~240s, capped at 1 hour
    }
}
```

### Attribute parameters

| Parameter | Default | Description |
|---|---|---|
| `attempts` | Required | Total executions including the first run. `1` and `0` both mean no retry — the job is dead-lettered immediately on failure. |
| `InitialDelay` | `null` | Delay before the first retry as a `TimeSpan` string, e.g. `"00:00:30"`. When omitted, the global `RetryDelayFactory` is used and `Multiplier`/`MaxDelay` are ignored. |
| `Multiplier` | `2.0` | Exponential backoff multiplier. Delay = `InitialDelay × Multiplier^(retry − 1)`. |
| `MaxDelay` | No cap | Upper bound on any single delay as a `TimeSpan` string, e.g. `"01:00:00"`. It is applied before the jitter, so a delay can end up to 10% above it. |
| `IgnoreRetryAttemptExceptions` | `null` | Exception types that must not be retried for this job. See [Failures that should not be retried](#failures-that-should-not-be-retried). |

A random ±10% jitter is applied to every delay computed from `InitialDelay` to spread retry spikes across workers.

### Immediate dead-letter on failure

Set `attempts` to `0` (or `1`) to skip all retries and send the job straight to dead-letter on its first failure. Use this for jobs where retrying cannot fix the problem — such as those that validate input data:

```csharp
[Retry(0)] // No retries — dead-letter immediately on any failure
public sealed class WebhookNotificationJob : IJob<WebhookInput>
{
    public async Task ExecuteAsync(WebhookInput input, CancellationToken ct)
    {
        // A failure here goes straight to the dead-letter handler
    }
}
```

## Execution timeout

Apply `[ExecutionTimeout]` to a job class to cap how long it may run, or set `options.DefaultExecutionTimeout` for all jobs (`null` by default, so it is opt-in; the attribute wins).

```csharp
[ExecutionTimeout("00:05:00")]
public sealed class ReportJob : IJob<ReportInput>
{
    public Task ExecuteAsync(ReportInput input, CancellationToken ct) => _http.GetAsync(input.Url, ct);
}
```

At the limit the job's `CancellationToken` is cancelled and the run fails with a `TimeoutException`, then follows the normal failure path: the attempt is used, it is retried, and on the last attempt it is dead-lettered.

- **Cancellation is cooperative.** A job that ignores its token keeps its worker slot until it returns, so pass `ct` to every HTTP, database and delay call.
- If the job is still running 10 seconds after the token was cancelled, NexJob logs one warning (`ignored cancellation ... still holds its worker slot`) and increments `nexjob.jobs.cancellation_ignored`. That is how you find the jobs that never look at their token. The job's outcome does not change.
- The timer starts after any `[Throttle]` slots are held. A shutdown is not a timeout: the job goes back to the queue without using the attempt.

## Per-job attempt limit

The same job type can need different limits depending on who enqueues it: a 2FA e-mail is useless after one failed try, a monthly invoice is worth ten. Pass `maxAttempts` when you enqueue or schedule:

```csharp
await scheduler.EnqueueAsync<SendEmailJob, EmailInput>(otpEmail, maxAttempts: 1);       // dead-letter on the first failure
await scheduler.EnqueueAsync<SendEmailJob, EmailInput>(invoiceEmail, maxAttempts: 8);
```

The limit for a job is, in order: the `maxAttempts` you passed, then `[Retry(n)]` on the class, then `options.MaxAttempts`. It is stored on the job, so the dashboard and `IJobContext.MaxAttempts` show the real value. Values below `1` throw `ArgumentOutOfRangeException` and nothing is stored. Recurring jobs do not take a per-job limit.

!!! note "One edge to know"
    A stored limit equal to the global `options.MaxAttempts` is indistinguishable from "not set", so on a class with `[Retry(n)]` the attribute applies. Passing exactly the global default as `maxAttempts` to such a job gives you `n`, not the default.

## Failures that should not be retried

Some failures cannot be fixed by trying again: invalid input, a business rule violation, a payload that does not deserialize. Retrying them only occupies workers and delays the alert. List them and the job goes straight to `Failed`, with the dead-letter handler and forwarders called, without waiting for the remaining attempts:

```csharp
builder.Services.AddNexJob(options =>
{
    // for every job
    options.IgnoreRetryAttemptExceptions = [typeof(ArgumentException), typeof(JsonException)];
});

// for one job type; combined with the global list
[Retry(5, IgnoreRetryAttemptExceptions = [typeof(InvalidPixKeyException)])]
public sealed class ProcessPixJob : IJob<PixInput> { ... }
```

- A listed type also matches the types derived from it, so `ArgumentException` covers `ArgumentNullException`. A base type of a listed type does not match.
- A listed exception wrapped by reflection (`TargetInvocationException`) or by a single-element `AggregateException` is unwrapped. An aggregate with several exceptions is not.
- This is not "ignore the error": the job still ends as `Failed`, and the attempt counts. An information log, `must not be retried`, says why it did not wait.
- It also works for the [execution timeout](#execution-timeout): list `typeof(TimeoutException)` to dead-letter a job that hit its limit instead of retrying it.
- Every entry must derive from `Exception`, or the attribute or the option throws `ArgumentException`.

## Custom retry delay factory

Replace the built-in delay curve entirely by assigning a delegate to `options.RetryDelayFactory`. The delegate receives the number of attempts already made (1 after the first failure) and returns the `TimeSpan` to wait before the next attempt. This affects all jobs that do not set their own `InitialDelay`.

```csharp
builder.Services.AddNexJob(options =>
{
    // Exponential backoff with up to 1 second of random jitter: 2s, 4s, 8s, ...
    options.RetryDelayFactory = attempt =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt))
        + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
});
```

## Next

- [Dead-letter handling](dead-letter.md): what runs when every attempt is spent.
- [Job context](../guides/job-context.md#progress-checkpoints-for-long-running-jobs): save a checkpoint so a retried attempt resumes where the previous one stopped.
- [Retries & dead letter](retries-and-dead-letter.md): how to choose a strategy for common failures.
