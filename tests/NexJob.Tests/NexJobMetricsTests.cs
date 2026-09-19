using System.Diagnostics.Metrics;
using FluentAssertions;
using NexJob.Telemetry;
using Xunit;

namespace NexJob.Tests;

[Collection("NexJobMetrics")]
public sealed class NexJobMetricsTests
{
    // ── N1: Positive paths ───────────────────────────────────────────────────

    [Fact]
    public void ObservableGauges_WhenProvidersConfigured_RecordExpectedMeasurements()
    {
        lock (NexJobMetrics.SyncLock)
        {
            try
            {
                // Arrange
                var activeWorkers = 3;
                var totalWorkers = 10;
                var queueMeasurements = new List<Measurement<long>>
                {
                    new(42, new KeyValuePair<string, object?>("nexjob.queue", "default")),
                    new(15, new KeyValuePair<string, object?>("nexjob.queue", "critical")),
                };

                NexJobMetrics.SetWorkerMetricsProviders(() => activeWorkers, () => totalWorkers);
                NexJobMetrics.SetQueueDepthProvider(() => queueMeasurements);

                using var listener = new MeterListener();
                int? recordedActive = null;
                int? recordedTotal = null;
                var recordedQueueDepths = new Dictionary<string, long>();

                listener.InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == NexJobMetrics.MeterName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                };

                listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
                {
                    if (instrument.Name == "nexjob.workers.active")
                    {
                        recordedActive = measurement;
                    }
                    else if (instrument.Name == "nexjob.workers.total")
                    {
                        recordedTotal = measurement;
                    }
                });

                listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                {
                    if (instrument.Name == "nexjob.queue.depth")
                    {
                        foreach (var tag in tags)
                        {
                            if (tag.Key == "nexjob.queue" && tag.Value is string q)
                            {
                                recordedQueueDepths[q] = measurement;
                            }
                        }
                    }
                });

                listener.Start();

                // Act
                listener.RecordObservableInstruments();

                // Assert
                recordedActive.Should().Be(3);
                recordedTotal.Should().Be(10);
                recordedQueueDepths.Should().ContainKey("default").WhoseValue.Should().Be(42);
                recordedQueueDepths.Should().ContainKey("critical").WhoseValue.Should().Be(15);
            }
            finally
            {
                NexJobMetrics.SetWorkerMetricsProviders(null, null);
                NexJobMetrics.SetQueueDepthProvider(null);
            }
        }
    }

    // ── N2: Negative paths ───────────────────────────────────────────────────

    [Fact]
    public void ObservableGauges_WhenProvidersNull_ReturnDefaultsWithoutThrowing()
    {
        lock (NexJobMetrics.SyncLock)
        {
            try
            {
                // Arrange
                NexJobMetrics.SetWorkerMetricsProviders(null, null);
                NexJobMetrics.SetQueueDepthProvider(null);

                using var listener = new MeterListener();
                int? recordedActive = null;
                int? recordedTotal = null;
                var queueDepthCount = 0;

                listener.InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == NexJobMetrics.MeterName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                };

                listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
                {
                    if (instrument.Name == "nexjob.workers.active")
                    {
                        recordedActive = measurement;
                    }
                    else if (instrument.Name == "nexjob.workers.total")
                    {
                        recordedTotal = measurement;
                    }
                });

                listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                {
                    if (instrument.Name == "nexjob.queue.depth")
                    {
                        queueDepthCount++;
                    }
                });

                listener.Start();

                // Act
                var act = () => listener.RecordObservableInstruments();

                // Assert
                act.Should().NotThrow();
                recordedActive.Should().Be(0);
                recordedTotal.Should().Be(0);
                queueDepthCount.Should().Be(0);
            }
            finally
            {
                NexJobMetrics.SetWorkerMetricsProviders(null, null);
                NexJobMetrics.SetQueueDepthProvider(null);
            }
        }
    }

    [Fact]
    public void ObservableGauges_WhenProviderThrows_CatchesAndReturnsSafeDefault()
    {
        lock (NexJobMetrics.SyncLock)
        {
            try
            {
                // Arrange
                NexJobMetrics.SetWorkerMetricsProviders(
                    () => throw new InvalidOperationException("Active workers fault"),
                    () => throw new InvalidOperationException("Total workers fault"));
                NexJobMetrics.SetQueueDepthProvider(
                    () => throw new InvalidOperationException("Queue depth fault"));

                using var listener = new MeterListener();
                int? recordedActive = null;
                int? recordedTotal = null;

                listener.InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == NexJobMetrics.MeterName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                };

                listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
                {
                    if (instrument.Name == "nexjob.workers.active")
                    {
                        recordedActive = measurement;
                    }
                    else if (instrument.Name == "nexjob.workers.total")
                    {
                        recordedTotal = measurement;
                    }
                });

                listener.Start();

                // Act
                var act = () => listener.RecordObservableInstruments();

                // Assert
                act.Should().NotThrow();
                recordedActive.Should().Be(0);
                recordedTotal.Should().Be(0);
            }
            finally
            {
                NexJobMetrics.SetWorkerMetricsProviders(null, null);
                NexJobMetrics.SetQueueDepthProvider(null);
            }
        }
    }

    // ── N3: Boundary and Inputs ──────────────────────────────────────────────

    [Fact]
    public void ObservableGauges_ZeroWorkersAndEmptyQueue_HandledCleanly()
    {
        lock (NexJobMetrics.SyncLock)
        {
            try
            {
                // Arrange
                NexJobMetrics.SetWorkerMetricsProviders(() => 0, () => 0);
                NexJobMetrics.SetQueueDepthProvider(() => Enumerable.Empty<Measurement<long>>());

                using var listener = new MeterListener();
                int? recordedActive = null;
                int? recordedTotal = null;
                var queueCount = 0;

                listener.InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == NexJobMetrics.MeterName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                };

                listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
                {
                    if (instrument.Name == "nexjob.workers.active")
                    {
                        recordedActive = measurement;
                    }
                    else if (instrument.Name == "nexjob.workers.total")
                    {
                        recordedTotal = measurement;
                    }
                });

                listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                {
                    if (instrument.Name == "nexjob.queue.depth")
                    {
                        queueCount++;
                    }
                });

                listener.Start();

                // Act
                listener.RecordObservableInstruments();

                // Assert
                recordedActive.Should().Be(0);
                recordedTotal.Should().Be(0);
                queueCount.Should().Be(0);
            }
            finally
            {
                NexJobMetrics.SetWorkerMetricsProviders(null, null);
                NexJobMetrics.SetQueueDepthProvider(null);
            }
        }
    }
}
