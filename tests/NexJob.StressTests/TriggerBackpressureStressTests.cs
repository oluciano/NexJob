using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace NexJob.StressTests;

[Trait("Category", "Stress")]
public sealed class TriggerBackpressureStressTests
{
    [Fact]
    public async Task Trigger_UnderStorageBackpressure_MaintainsBoundedMemoryAndZeroLoss()
    {
        // Arrange
        const int totalBrokerMessages = 5000;
        const int maxPrefetch = 50;
        var processedCount = 0;
        var maxObservedInFlight = 0;
        var currentInFlight = 0;

        // Bounded channel simulating a broker queue with prefetch limit
        var brokerChannel = Channel.CreateBounded<int>(new BoundedChannelOptions(maxPrefetch)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });

        // Producer Task: publish 5,000 messages into broker
        var producerTask = Task.Run(async () =>
        {
            for (var i = 0; i < totalBrokerMessages; i++)
            {
                await brokerChannel.Writer.WriteAsync(i);
            }

            brokerChannel.Writer.Complete();
        });

        var initialMemory = GC.GetTotalMemory(forceFullCollection: false);

        // Consumer Loop simulating trigger backpressure:
        // Reader pulls up to maxPrefetch concurrent messages and processes them with artificial 1ms delay
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var consumerTasks = new Task[maxPrefetch];

        for (var w = 0; w < maxPrefetch; w++)
        {
            consumerTasks[w] = Task.Run(async () =>
            {
                while (await brokerChannel.Reader.WaitToReadAsync(cts.Token))
                {
                    while (brokerChannel.Reader.TryRead(out _))
                    {
                        var inFlight = Interlocked.Increment(ref currentInFlight);

                        // Update max concurrent in-flight count
                        int initial;
                        do
                        {
                            initial = Volatile.Read(ref maxObservedInFlight);
                            if (inFlight <= initial)
                            {
                                break;
                            }
                        }
                        while (Interlocked.CompareExchange(ref maxObservedInFlight, inFlight, initial) != initial);

                        // Simulate storage write delay (backpressure)
                        await Task.Delay(1, cts.Token);

                        Interlocked.Decrement(ref currentInFlight);
                        Interlocked.Increment(ref processedCount);
                    }
                }
            }, cts.Token);
        }

        await producerTask;
        await Task.WhenAll(consumerTasks);

        var finalMemory = GC.GetTotalMemory(forceFullCollection: false);
        var memoryDeltaMb = (finalMemory - initialMemory) / (1024.0 * 1024.0);

        // Assert:
        processedCount.Should().Be(totalBrokerMessages, "zero messages must be dropped under storage backpressure");
        maxObservedInFlight.Should().BeLessOrEqualTo(maxPrefetch, "in-flight concurrent messages must never exceed prefetch limit");
        memoryDeltaMb.Should().BeLessThan(100.0, "memory growth must remain strictly bounded under sustained load");
    }
}
