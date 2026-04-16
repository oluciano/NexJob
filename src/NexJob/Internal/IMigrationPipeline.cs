namespace NexJob.Internal;

/// <summary>
/// Interface for the migration pipeline that upgrades job payloads between schema versions.
/// </summary>
internal interface IMigrationPipeline
{
    /// <summary>
    /// Migrates the input JSON from the stored version to the current version.
    /// </summary>
    /// <param name="inputJson">The input json.</param>
    /// <param name="storedVersion">The stored version.</param>
    /// <param name="currentVersion">The current version.</param>
    /// <param name="inputType">Type of the input.</param>
    /// <returns>The migrated JSON.</returns>
    string Migrate(string inputJson, int storedVersion, int currentVersion, Type inputType);
}
