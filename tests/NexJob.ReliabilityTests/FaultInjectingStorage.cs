using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Makes the next calls of one storage method fail, on a real provider, and forwards everything else. Used to inject
/// the failure a database can have at the one moment a test cares about.
/// </summary>
/// <typeparam name="T">The storage interface the proxy implements.</typeparam>
public class FaultInjectingStorage<T> : DispatchProxy
    where T : class
{
    private T _inner = null!;
    private FaultPlan _plan = null!;

    /// <summary>Replaces the registration of <typeparamref name="T"/> with a proxy over the original one.</summary>
    /// <param name="services">The service collection, after the storage was registered.</param>
    /// <param name="plan">The failures to inject.</param>
    public static void Decorate(IServiceCollection services, FaultPlan plan)
    {
        var original = services.Last(d => d.ServiceType == typeof(T));
        services.Remove(original);
        services.AddSingleton(sp =>
        {
            var inner = Resolve(original, sp);
            var proxy = Create<T, FaultInjectingStorage<T>>();
            var typed = (FaultInjectingStorage<T>)(object)proxy;
            typed._inner = inner;
            typed._plan = plan;
            return proxy;
        });
    }

    private static T Resolve(ServiceDescriptor original, IServiceProvider sp)
    {
        if (original.ImplementationFactory is not null)
        {
            return (T)original.ImplementationFactory(sp);
        }

        if (original.ImplementationInstance is not null)
        {
            return (T)original.ImplementationInstance;
        }

        return (T)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!);
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (string.Equals(targetMethod.Name, _plan.Method, StringComparison.Ordinal) && _plan.TryFail())
        {
            return Faulted(targetMethod.ReturnType, new InvalidOperationException($"injected failure of {_plan.Method}"));
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
            throw;
        }
    }

    private static object Faulted(Type returnType, Exception exception)
    {
        if (returnType == typeof(Task))
        {
            return Task.FromException(exception);
        }

        var result = returnType.GetGenericArguments()[0];
        return typeof(Task)
            .GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!
            .MakeGenericMethod(result)
            .Invoke(null, [exception])!;
    }
}

/// <summary>Which storage method fails, and how many times.</summary>
public sealed class FaultPlan(string method, int failures)
{
    private int _remaining = failures;
    private int _injected;

    /// <summary>Gets the name of the storage method to fail.</summary>
    public string Method { get; } = method;

    /// <summary>Gets how many calls failed so far.</summary>
    public int Injected => Volatile.Read(ref _injected);

    /// <summary>Takes one failure, when any is left.</summary>
    /// <returns>True when this call has to fail.</returns>
    public bool TryFail()
    {
        if (Interlocked.Decrement(ref _remaining) < 0)
        {
            return false;
        }

        Interlocked.Increment(ref _injected);
        return true;
    }
}
