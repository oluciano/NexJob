using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace NexJob.Internal;

/// <summary>
/// Internal registry of all job types registered via <see cref="NexJobServiceCollectionExtensions.AddNexJobJobs(IServiceCollection, System.Reflection.Assembly)"/>.
/// Used to resolve job simple names to their <see cref="Type"/> at recurring job configuration time.
/// </summary>
internal sealed class NexJobJobRegistry
{
    private readonly HashSet<Type> _types = [];

    /// <summary>
    /// All registered job types.
    /// </summary>
    public IReadOnlyCollection<Type> Types => _types;

    /// <summary>
    /// Registers a job type in the registry.
    /// </summary>
    public void Register(Type type) => _types.Add(type);
}
