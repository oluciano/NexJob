namespace NexJob.Internal;

/// <summary>
/// Thrown inside the executor when a job could not get a free slot of its throttled resource within the allowed
/// wait. It is never a job failure: the job is returned to the queue without consuming an attempt.
/// </summary>
internal sealed class ThrottleDeferredException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="ThrottleDeferredException"/> class.</summary>
    /// <param name="resource">The throttled resource whose slot was not available.</param>
    /// <param name="waited">How long the job waited for the slot.</param>
    public ThrottleDeferredException(string resource, TimeSpan waited)
        : base($"No free slot for throttled resource '{resource}' after {waited.TotalSeconds:0.#}s; the job was returned to the queue.")
    {
        Resource = resource;
        Waited = waited;
    }

    /// <summary>Gets the throttled resource whose slot was not available.</summary>
    public string Resource { get; }

    /// <summary>Gets how long the job waited for the slot.</summary>
    public TimeSpan Waited { get; }
}
