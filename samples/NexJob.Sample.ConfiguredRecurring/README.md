# NexJob.Sample.ConfiguredRecurring

A minimal WebAPI sample demonstrating NexJob's **automatic recurring job binding from `appsettings.json`** — Zero-code configuration approach.

## What It Does

- **TesteOnlyJob**: A simple job that implements `IJob` (no input required) and logs the current time every minute
- **Configuration-driven**: The recurring job is defined entirely in `appsettings.json` — no code registration needed
- **Dashboard**: Full NexJob dashboard at `/dashboard` for real-time job monitoring

## Running the Sample

```bash
cd samples/NexJob.Sample.ConfiguredRecurring
dotnet run
```

The application will start on `http://localhost:5000` and automatically register the recurring job from `appsettings.json`.

## Key Features

### 1. Job Implementation
```csharp
public sealed class TesteOnlyJob(ILogger<TesteOnlyJob> logger) : IJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        logger.LogInformation("[TesteOnlyJob] Executing at {Time}", now);
        Console.WriteLine($"[TesteOnlyJob] Current time: {now}");
        await Task.CompletedTask;
    }
}
```

### 2. Configuration-Driven Recurring Jobs
No code registration required. Just define in `appsettings.json`:

```json
{
  "NexJob": {
    "RecurringJobs": [
      {
        "Id": "log-time-every-minute",
        "JobType": "NexJob.Sample.ConfiguredRecurring.Jobs.TesteOnlyJob, NexJob.Sample.ConfiguredRecurring",
        "Cron": "*/1 * * * *",
        "Queue": "default",
        "TimeZoneId": "America/Sao_Paulo",
        "Enabled": true
      }
    ]
  }
}
```

### 3. Dashboard

Once running, visit the dashboard at:
```
http://localhost:5000/dashboard
```

Watch the job execute every minute — see execution timeline, status, metrics, and logs in real-time.

## What's Happening

1. **Startup**: NexJob auto-registers the recurring job from `appsettings.json`
2. **Every 1 minute**: The job executes and logs the current time
3. **Dashboard**: Shows job history, status, execution timeline, and metrics
4. **Zero code**: No manual scheduler registration needed

## API Endpoints

- `GET /` — Health check
- `GET /dashboard` — NexJob Dashboard

## Project Structure

```
NexJob.Sample.ConfiguredRecurring/
├── Jobs/
│   └── TesteOnlyJob.cs                           # IJob implementation
├── Program.cs                                     # NexJob setup & dashboard
├── appsettings.json                              # Recurring job configuration
├── NexJob.Sample.ConfiguredRecurring.csproj      # Project file
└── README.md                                      # This file
```

## Customization

### Change the Schedule
Edit `appsettings.json`:
```json
"Cron": "0 9 * * *"  // Run daily at 9:00 AM
```

### Add More Jobs
1. Create a new job class implementing `IJob` or `IJob<TInput>`
2. Add an entry to `RecurringJobs` in `appsettings.json`
3. The job is auto-registered on startup

### Multiple Workers
Change `Workers` in `appsettings.json`:
```json
"Workers": 10
```
