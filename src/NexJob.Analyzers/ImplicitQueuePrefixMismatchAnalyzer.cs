using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that detects EnqueueAsync / ScheduleAsync calls without explicit queue parameter in multi-service environments.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ImplicitQueuePrefixMismatchAnalyzer : MissingSchedulerArgumentAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ016";

    private static readonly DiagnosticDescriptor Descriptor = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Enqueue without explicit queue may cause cross-service prefix mismatch",
        "Enqueue call without explicit 'queue' parameter targets default queue; in multi-service architectures with distinct QueuePrefix, specify 'queue' explicitly or align QueuePrefix",
        "Reliability",
        "When services share storage with custom QueuePrefix settings, enqueuing without explicit queue parameter sends jobs to the producer's default queue, which consumer services may never poll.",
        isEnabledByDefault: false);

    /// <summary>
    /// Initializes a new instance of the <see cref="ImplicitQueuePrefixMismatchAnalyzer"/> class.
    /// </summary>
    public ImplicitQueuePrefixMismatchAnalyzer()
        : base("queue")
    {
    }

    /// <inheritdoc/>
    protected override DiagnosticDescriptor Rule => Descriptor;
}
