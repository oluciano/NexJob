using Microsoft.Extensions.DependencyInjection;

namespace NexJob;

/// <summary>
/// Builder for configuring NexJob services.
/// </summary>
public sealed class NexJobBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NexJobBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public NexJobBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; }
}
