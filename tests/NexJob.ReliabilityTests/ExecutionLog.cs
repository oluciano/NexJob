namespace NexJob.ReliabilityTests;

/// <summary>
/// Ordered, thread-safe record of what the jobs of one test host did, so a test can assert on order as well as on
/// presence.
/// </summary>
internal sealed class ExecutionLog
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public void Add(string entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    public int Count(string entry) => Entries.Count(e => string.Equals(e, entry, StringComparison.Ordinal));
}
