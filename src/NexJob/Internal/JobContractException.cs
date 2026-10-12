namespace NexJob.Internal;

/// <summary>
/// Thrown when a job type does not implement the contract its stored input type requires, for example a job that
/// implements <see cref="IJob"/> receiving a trigger message stored as <see cref="string"/>. Retrying cannot fix it.
/// </summary>
internal sealed class JobContractException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JobContractException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public JobContractException(string message)
        : base(message)
    {
    }
}
