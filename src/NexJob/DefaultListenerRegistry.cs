using System.Collections.Concurrent;

namespace NexJob;

/// <summary>
/// Default thread-safe in-memory implementation of <see cref="IListenerRegistry"/>.
/// </summary>
public sealed class DefaultListenerRegistry : IListenerRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void Register(ListenerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (string.IsNullOrWhiteSpace(registration.Id))
        {
            throw new ArgumentException("Listener ID cannot be empty or whitespace.", nameof(registration));
        }

        var now = DateTimeOffset.UtcNow;
        _entries.AddOrUpdate(
            registration.Id,
            _ => new Entry(registration, now),
            (_, existing) =>
            {
                var entry = new Entry(registration, existing.StartedAt)
                {
                    Status = existing.Status,
                    StatusDescription = existing.StatusDescription,
                    StatusChangedAt = existing.StatusChangedAt,
                };
                return entry;
            });
    }

    /// <inheritdoc/>
    public void UpdateStatus(string listenerId, ListenerStatus status, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(listenerId))
        {
            return;
        }

        if (_entries.TryGetValue(listenerId, out var entry))
        {
            lock (entry)
            {
                entry.Status = status;
                entry.StatusDescription = description;
                entry.StatusChangedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <inheritdoc/>
    public ListenerSnapshot? Get(string listenerId)
    {
        if (string.IsNullOrWhiteSpace(listenerId))
        {
            return null;
        }

        if (_entries.TryGetValue(listenerId, out var entry))
        {
            lock (entry)
            {
                return entry.ToSnapshot();
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ListenerSnapshot> GetAll()
    {
        var list = new List<ListenerSnapshot>(_entries.Count);
        foreach (var entry in _entries.Values)
        {
            lock (entry)
            {
                list.Add(entry.ToSnapshot());
            }
        }

        return list;
    }

    private sealed class Entry
    {
        public Entry(ListenerRegistration registration, DateTimeOffset now)
        {
            Registration = registration;
            Status = ListenerStatus.Starting;
            StatusDescription = null;
            StartedAt = now;
            StatusChangedAt = now;
        }

        public ListenerRegistration Registration { get; }

        public ListenerStatus Status { get; set; }

        public string? StatusDescription { get; set; }

        public DateTimeOffset StartedAt { get; }

        public DateTimeOffset StatusChangedAt { get; set; }

        public ListenerSnapshot ToSnapshot() =>
            new(
                Id: Registration.Id,
                Broker: Registration.Broker,
                Endpoint: Registration.Endpoint,
                TargetJobType: Registration.TargetJobType,
                Status: Status,
                StatusDescription: StatusDescription,
                JobTag: Registration.JobTag,
                ConsumerGroup: Registration.ConsumerGroup,
                StartedAt: StartedAt,
                StatusChangedAt: StatusChangedAt);
    }
}
