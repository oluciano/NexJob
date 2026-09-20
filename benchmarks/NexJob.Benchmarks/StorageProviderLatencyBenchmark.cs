using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Postgres;
using NexJob.Redis;
using StackExchange.Redis;

namespace NexJob.Benchmarks;

/// <summary>
/// Compares enqueue latency across InMemory, Redis, and PostgreSQL storage providers.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class StorageProviderLatencyBenchmark
{
    private IHost _inMemoryHost = null!;
    private IScheduler _inMemoryScheduler = null!;

    private IHost? _redisHost;
    private IScheduler? _redisScheduler;

    private IHost? _postgresHost;
    private IScheduler? _postgresScheduler;

    private NoOpInput _input = null!;

    /// <summary>
    /// Initializes available storage providers for comparative microbenchmarks.
    /// </summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _input = new NoOpInput { Payload = "benchmark-payload", };

        // 1. InMemory Provider
        _inMemoryHost = Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromSeconds(60);
                });
                s.AddTransient<NoOpJob>();
            })
            .Build();
        await _inMemoryHost.StartAsync();
        _inMemoryScheduler = _inMemoryHost.Services.GetRequiredService<IScheduler>();

        // 2. Redis Provider (if available)
        var redisConn = Environment.GetEnvironmentVariable("BENCHMARK_REDIS") ?? "localhost:6379";
        try
        {
            var multiplexer = await ConnectionMultiplexer.ConnectAsync(redisConn);
            if (multiplexer.IsConnected)
            {
                _redisHost = Host.CreateDefaultBuilder()
                    .ConfigureServices(s =>
                    {
                        s.AddNexJobRedis(redisConn);
                        s.AddNexJob(opt =>
                        {
                            opt.Workers = 1;
                            opt.PollingInterval = TimeSpan.FromSeconds(60);
                        });
                        s.AddTransient<NoOpJob>();
                    })
                    .Build();
                await _redisHost.StartAsync();
                _redisScheduler = _redisHost.Services.GetRequiredService<IScheduler>();
            }
        }
        catch
        {
            _redisHost = null;
            _redisScheduler = null;
        }

        // 3. Postgres Provider (if available)
        var pgConn = Environment.GetEnvironmentVariable("BENCHMARK_POSTGRES");
        if (!string.IsNullOrWhiteSpace(pgConn))
        {
            try
            {
                _postgresHost = Host.CreateDefaultBuilder()
                    .ConfigureServices(s =>
                    {
                        s.AddNexJobPostgres(pgConn);
                        s.AddNexJob(opt =>
                        {
                            opt.Workers = 1;
                            opt.PollingInterval = TimeSpan.FromSeconds(60);
                        });
                        s.AddTransient<NoOpJob>();
                    })
                    .Build();
                await _postgresHost.StartAsync();
                _postgresScheduler = _postgresHost.Services.GetRequiredService<IScheduler>();
            }
            catch
            {
                _postgresHost = null;
                _postgresScheduler = null;
            }
        }
    }

    /// <summary>
    /// Stops running hosts.
    /// </summary>
    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _inMemoryHost.StopAsync();
        if (_redisHost != null)
        {
            await _redisHost.StopAsync();
        }

        if (_postgresHost != null)
        {
            await _postgresHost.StopAsync();
        }
    }

    /// <summary>
    /// Measures InMemory enqueue latency via IScheduler.
    /// </summary>
    [Benchmark(Baseline = true)]
    public Task InMemory_Enqueue()
        => _inMemoryScheduler.EnqueueAsync<NoOpJob, NoOpInput>(_input);

    /// <summary>
    /// Measures Redis enqueue latency via IScheduler (if connected).
    /// </summary>
    [Benchmark]
    public Task Redis_Enqueue()
    {
        if (_redisScheduler is null)
        {
            return Task.CompletedTask;
        }

        return _redisScheduler.EnqueueAsync<NoOpJob, NoOpInput>(_input);
    }

    /// <summary>
    /// Measures PostgreSQL enqueue latency via IScheduler (if connected).
    /// </summary>
    [Benchmark]
    public Task Postgres_Enqueue()
    {
        if (_postgresScheduler is null)
        {
            return Task.CompletedTask;
        }

        return _postgresScheduler.EnqueueAsync<NoOpJob, NoOpInput>(_input);
    }
}
