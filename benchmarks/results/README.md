# NexJob Benchmark Results

BenchmarkDotNet v0.14.0, Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon CPU E5-2667 v4 3.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 8.0.125 · .NET 8.0.25 (8.0.2526.11203), X64 RyuJIT AVX2
Run: March 2026

---

## EnqueueLatencyBenchmark

Single job enqueue, in-memory storage.

| Method                 | Job      | Mean      | Error     | StdDev    | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|----------------------- |--------- |----------:|----------:|----------:|------:|-------:|-------:|----------:|------------:|
| NexJob_SingleEnqueue   | .NET 8.0 | 12.713 μs | 0.9973 μs | 2.8453 μs |  1.08 | 0.0610 | 0.0305 |   2.77 KB |        1.00 |
| Hangfire_SingleEnqueue | .NET 8.0 | 35.817 μs | 1.3414 μs | 3.9552 μs |  3.03 | 0.8850 | 0.2136 |  11.31 KB |        4.09 |
|                        |          |           |           |           |       |        |        |           |             |
| NexJob_SingleEnqueue   | ShortRun |  9.283 μs | 6.9796 μs | 0.3826 μs |  1.00 | 0.0610 |      - |   1.67 KB |        1.00 |
| Hangfire_SingleEnqueue | ShortRun | 26.629 μs | 6.5590 μs | 0.3595 μs |  2.87 | 0.8545 | 0.2136 |   11.2 KB |        6.70 |

**ShortRun summary:** NexJob is **2.87x faster** and uses **85% less memory** per enqueue.

---

## ThroughputBenchmark

> Note: The Hangfire throughput benchmark was not included in published results because
> Hangfire has no built-in mechanism to await job completion from the enqueue side.
> The benchmark measured different things (NexJob waited for all 500 jobs to finish;
> Hangfire measured enqueue-only), making the comparison invalid.
> Run `dotnet run -c Release --project benchmarks/NexJob.Benchmarks -- --filter '*Throughput*'`
> to measure locally.
