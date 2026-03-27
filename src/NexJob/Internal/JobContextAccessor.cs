namespace NexJob.Internal;

/// <summary>Default implementation of <see cref="IJobContextAccessor"/>.</summary>
internal sealed class JobContextAccessor : IJobContextAccessor
{
    /// <inheritdoc/>
    public IJobContext? Context { get; set; }
}
