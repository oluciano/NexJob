using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NexJob.Analyzers;

/// <summary>
/// Diagnostic analyzer that recommends specifying an idempotencyKey when enqueueing background jobs.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EncourageIdempotencyKeyAnalyzer : MissingSchedulerArgumentAnalyzer
{
    /// <summary>
    /// The diagnostic identifier.
    /// </summary>
    public const string DiagnosticId = "NXJ011";

    private static readonly DiagnosticDescriptor Descriptor = AnalyzerHelper.CreateDescriptor(
        DiagnosticId,
        "Consider providing an idempotencyKey for background jobs",
        "Enqueue call does not specify an 'idempotencyKey'; provide an idempotencyKey to prevent duplicate job execution upon network retries or producer replay",
        "Design",
        "In distributed architectures and event-driven triggers, transient network blips and message replays can cause duplicate jobs. Supplying an idempotencyKey guarantees exactly-once active job semantics.",
        isEnabledByDefault: false);

    /// <summary>
    /// Initializes a new instance of the <see cref="EncourageIdempotencyKeyAnalyzer"/> class.
    /// </summary>
    public EncourageIdempotencyKeyAnalyzer()
        : base("idempotencyKey")
    {
    }

    /// <inheritdoc/>
    protected override DiagnosticDescriptor Rule => Descriptor;
}
