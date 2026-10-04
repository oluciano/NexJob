namespace NexJob.ReliabilityTests;

/// <summary>
/// Holds a <see cref="GatedParentJob"/> inside its execution until the test opens the gate, either to succeed or
/// to fail.
/// </summary>
internal sealed class ParentGate
{
    private readonly TaskCompletionSource<bool> _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<bool> Opened => _open.Task;

    public void OpenToSucceed() => _open.TrySetResult(true);

    public void OpenToFail() => _open.TrySetResult(false);
}
