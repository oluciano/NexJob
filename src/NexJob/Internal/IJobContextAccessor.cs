namespace NexJob.Internal;

/// <summary>
/// Scoped holder that bridges the job execution pipeline with the <see cref="IJobContext"/>
/// DI registration. Set by <see cref="JobDispatcherService"/> before invoking the job,
/// then resolved by consumers via the <see cref="IJobContext"/> service.
/// </summary>
internal interface IJobContextAccessor
{
    /// <summary>The context for the currently executing job, or <see langword="null"/> outside execution.</summary>
    IJobContext? Context { get; set; }
}
