namespace NexJob.DocsTruth;

/// <summary>A phrase that stopped being true in a version.</summary>
/// <param name="Name">A short name for the report.</param>
/// <param name="Pattern">A regular expression.</param>
/// <param name="Since">The version from which the phrase is wrong.</param>
internal sealed record StaleTerm(string Name, string Pattern, string Since);
