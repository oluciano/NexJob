# NexJob Performance Benchmarks

Comparative performance benchmarks between **NexJob** and **Hangfire** powered by [BenchmarkDotNet](https://benchmarkdotnet.org/).

---

## Test Suites

| Benchmark | Focus | Parameters | Description |
|---|---|---|---|
| [`EnqueueLatencyBenchmark`](EnqueueLatencyBenchmark.cs) | Enqueue Latency & Memory | `PayloadBytes: 0, 1024, 10240` | Measures single enqueue latency, memory allocation, and GC collections. |
| [`ConcurrentEnqueueBenchmark`](ConcurrentEnqueueBenchmark.cs) | Concurrency & Lock Contention | `ConcurrencyLevel: 10, 50` | Enqueues 1,000 jobs concurrently across parallel worker threads. |
| [`DispatchLatencyBenchmark`](DispatchLatencyBenchmark.cs) | Wake-up Channel Latency | Baseline | Measures elapsed ticks from `EnqueueAsync` until `ExecuteAsync` starts via `JobWakeUpChannel`. |
| [`ThroughputBenchmark`](ThroughputBenchmark.cs) | End-to-End Processing | 500 jobs / 20 workers | Measures time to enqueue and process 500 fire-and-forget jobs to completion. |

---

## Running Benchmarks

### List available benchmarks
```bash
dotnet run -c Release -- --list flat
```

### Run single enqueue latency benchmark
```bash
dotnet run -c Release -- --filter *EnqueueLatencyBenchmark*
```

### Run concurrent enqueue benchmark
```bash
dotnet run -c Release -- --filter *ConcurrentEnqueueBenchmark*
```

### Run quick iteration (ShortRun)
```bash
dotnet run -c Release -- --filter *EnqueueLatencyBenchmark* --job short
```

---

## Benchmark Results (.NET 8 RyuJIT AVX2)

Measured on .NET 8 (Ubuntu 24.04, Intel Xeon 3.20 GHz, in-memory baseline):

### Single Enqueue Latency & Memory (Default Job)

| Method | Mean Latency | Gen0 / 1k ops | Gen1 / 1k ops | Memory Allocated |
|---|---|---|---|---|
| **NexJob** (`NexJob_SingleEnqueue`) | **13.35 µs** | **0.0610** | **0.0000** | **2.10 KB** |
| **Hangfire** (`Hangfire_SingleEnqueue`) | **35.95 µs** | **0.8545** | **0.1831** | **11.20 KB** |

- **Latency:** NexJob is **2.7× faster**.
- **Memory:** NexJob allocates **81% less memory** per enqueue.
- **GC Overhead:** NexJob produces **zero Gen1 collections** and 14× fewer Gen0 collections.
