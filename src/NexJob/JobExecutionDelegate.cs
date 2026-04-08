namespace NexJob;

/// <summary>
/// Represents the next component in the job execution filter pipeline.
/// Invoke this delegate from within an <see cref="IJobExecutionFilter"/> to
/// pass control to the next filter, or to the job itself when no more filters remain.
/// </summary>
public delegate Task JobExecutionDelegate(CancellationToken cancellationToken);
