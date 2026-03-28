namespace NexJob.Sample.WorkerService.Jobs;

/// <summary>
/// Specifies the target resource to be cleaned up.
/// </summary>
public record CleanupInput(string Target);
