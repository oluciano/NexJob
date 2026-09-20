namespace NexJob.StressTests;

public sealed record StressJobInput
{
    public int Index { get; init; }

    public string Payload { get; init; } = string.Empty;
}
