namespace NexJob.Internal;

/// <summary>
/// Sentinel class used internally to represent jobs with no input.
/// When serializing <see cref="IJob"/> implementations (which have no input type parameter),
/// the input is serialized as an instance of this class.
/// This type is part of the internal job serialization mechanism and should not be used directly by applications.
/// </summary>
public sealed class NoInput
{
    /// <summary>
    /// Gets the singleton instance of <see cref="NoInput"/>.
    /// </summary>
    public static readonly NoInput Instance = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="NoInput"/> class.
    /// This constructor is required for JSON deserialization support.
    /// </summary>
    public NoInput()
    {
    }
}
