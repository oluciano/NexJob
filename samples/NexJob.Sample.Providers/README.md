# NexJob.Sample.Providers

The same small app on any NexJob storage provider. `ProviderSetup.cs` holds the registration for each one, so it doubles as
a copy-paste reference.

## Run

InMemory (default, nothing to install):

```bash
dotnet run --project samples/NexJob.Sample.Providers/NexJob.Sample.Providers.csproj
```

Another provider: start its database (from the `samples` directory), then choose it with `Sample:Provider`:

```bash
docker compose up -d postgres        # or sqlserver, redis, mongodb
dotnet run --project samples/NexJob.Sample.Providers/NexJob.Sample.Providers.csproj -- --Sample:Provider=Postgres
```

`Sample:Provider` is one of `InMemory`, `Postgres`, `SqlServer`, `Redis`, `MongoDB` (an environment variable works too:
`Sample__Provider=Redis`). URL: `http://localhost:5012`

| Provider | Connection string key | Default (matches `docker-compose.yml`) |
|---|---|---|
| Postgres | `ConnectionStrings:NexJobPostgres` | `Host=localhost;Port=5432;Database=nexjob_sample;...` |
| SqlServer | `ConnectionStrings:NexJobSqlServer` | `Server=localhost,1433;Database=master;User Id=sa;...` |
| Redis | `ConnectionStrings:NexJobRedis` | `localhost:6379` |
| MongoDB | `ConnectionStrings:NexJobMongoDB` | `mongodb://localhost:27017` (database `nexjob_sample`) |

## Endpoints

| Call | What to expect |
|---|---|
| `GET /provider` | `{ "provider": "Postgres" }`, the one in use |
| `POST /jobs?message=hello` | `202` with a `jobId`; the job logs the message |
| `GET /jobs/{id}` | `Succeeded` after a moment, on every provider |
| `GET /dashboard` | The dashboard on the chosen storage |

## When it cannot start

An unknown provider, or a provider whose connection string is missing, stops the app with a one-line message naming
what to fix. If the database is not running, the app says so and prints the `docker compose` command, instead of a stack
trace.
