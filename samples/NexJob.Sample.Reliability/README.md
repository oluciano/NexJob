# NexJob.Sample.Reliability

One endpoint per reliability behavior, on in-memory storage (no infrastructure needed). Open the dashboard at
`http://localhost:5011/dashboard` while you try them.

## Run

```bash
dotnet run --project samples/NexJob.Sample.Reliability/NexJob.Sample.Reliability.csproj
```

URL: `http://localhost:5011`

## Endpoints

| Call | What it shows | What to expect |
|---|---|---|
| `POST /retry/flaky` | `[Retry(4, InitialDelay = "00:00:01")]` | The job fails twice and succeeds on attempt 3. `GET /jobs/{jobId}` shows `Succeeded`, `attempts: 3`. |
| `POST /checkpoint` | `IJobContext.SaveCheckpointAsync` / `GetCheckpoint<T>()` | The job "crashes" after step 3; the retry resumes at step 4. `GET /log` shows steps 1 to 5 once each. |
| `POST /deadletter` | `IDeadLetterHandler<TJob>` | Two attempts fail, the job is `Failed` and `GET /log` shows a `deadletter:` entry. |
| `POST /deadline/demo`, then wait 3 s, then `POST /queues/held/resume` | `deadlineAfter` and pausing a queue | The job waited in a paused queue longer than its 2 second deadline, so it ends `Expired` and never runs. |
| `POST /circuit/trip`, then `POST /circuit/probe` | Queue circuit breaker (`EnableCircuitBreaker`, 3 failures) | The three jobs fail and open the circuit of the `fragile` queue; the probe stays `Enqueued` (the dashboard shows the circuit open). |
| `POST /queues/fragile/reset-circuit` | `IJobControlService.ResetQueueCircuitAsync` | The circuit closes and the probe runs (`Succeeded`). |
| `POST /queues/{queue}/pause`, `POST /queues/{queue}/resume` | `PauseQueueAsync` / `ResumeQueueAsync` | Pausing takes effect on each node's next polling cycle; running jobs are not interrupted. |
| `POST /jobs/{id}/requeue`, `DELETE /jobs/{id}` | `RequeueJobAsync` / `DeleteJobAsync` | Requeue a failed job; delete one for good. |
| `GET /health` | `AddHealthChecks().AddNexJob()` | `200 Healthy` while storage is reachable. |
| `GET /jobs/{id}`, `GET /log` | Reading state | Status and attempts of a job; what the demo jobs did, in order. |

## Configuration

`appsettings.json` polls three queues: `default`, `fragile` (circuit breaker, set in `Program.cs`) and `held` (the one
the deadline demo pauses). The polling interval is 200 ms so the demos are quick.
