namespace NexJob.ReliabilityTests;

/// <summary>Names the host a job ran on, so a test can tell which node did the work.</summary>
internal sealed class NodeName(string value)
{
    public string Value { get; } = value;
}
