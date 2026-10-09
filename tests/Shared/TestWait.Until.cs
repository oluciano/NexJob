namespace NexJob.Tests;

/// <summary>
/// Waits for an observed condition instead of sleeping a fixed time, which is not enough on a loaded machine.
/// This file is shared: <c>NexJob.Tests</c> and the integration test projects link it.
/// </summary>
internal static partial class TestWait
{
    /// <summary>Polls <paramref name="condition"/> until it returns true, or fails after <paramref name="timeout"/>.</summary>
    /// <param name="condition">The state the test needs before it asserts.</param>
    /// <param name="timeout">How long to wait; defaults to 10 seconds.</param>
    /// <param name="because">What the test is waiting for; named in the failure message.</param>
    /// <returns>A task that completes once the condition holds.</returns>
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null, string? because = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!await condition().ConfigureAwait(false))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    because is null
                        ? "The awaited condition did not hold before the timeout."
                        : $"The awaited condition did not hold before the timeout: {because}.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }
}
