namespace NexJob.ReliabilityTests;

/// <summary>
/// Present only in the crash worker process. A <see cref="CrashTargetJob"/> that finds it writes the file to say it has
/// started and then never finishes, so the process can be killed in the middle of the job.
/// </summary>
internal sealed class CrashMarker(string path)
{
    public string Path { get; } = path;
}
