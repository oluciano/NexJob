using Microsoft.Extensions.DependencyInjection;

namespace NexJob.Internal;

/// <summary>
/// Contains the scoped artifacts required to execute one job invocation.
/// </summary>
internal sealed record JobInvocationContext(
    IServiceScope Scope,
    object JobInstance,
    object Input,
    Func<object, object, CancellationToken, Task> Invoker,
    IEnumerable<ThrottleAttribute> ThrottleAttributes,
    RetentionAttribute? RetentionAttribute = null) : IDisposable
{
    /// <inheritdoc/>
    public void Dispose() => Scope.Dispose();
}
