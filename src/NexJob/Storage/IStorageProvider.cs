namespace NexJob.Storage;

/// <summary>
/// Full storage contract combining job execution, recurring scheduling,
/// and dashboard query capabilities. Implement this interface to create
/// a complete NexJob storage adapter.
/// </summary>
/// <remarks>
/// For advanced scenarios (read replicas, separation of concerns), implement
/// <see cref="IJobStorage"/>, <see cref="IRecurringStorage"/>, and
/// <see cref="IDashboardStorage"/> independently and register each via DI.
/// </remarks>
public interface IStorageProvider : IJobStorage, IRecurringStorage, IDashboardStorage
{
}
