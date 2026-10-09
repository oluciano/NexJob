namespace NexJob.DocsTruth;

/// <summary>One difference between the code and the documentation.</summary>
/// <param name="Check">The check that found it (for example <c>metrics</c>).</param>
/// <param name="Subject">What it is about (a metric, a property, a file and line).</param>
/// <param name="Message">What is wrong.</param>
/// <param name="Severity">How it is treated by <c>--strict</c>.</param>
internal sealed record Finding(string Check, string Subject, string Message, Severity Severity = Severity.Drift);
