namespace NexJob.SqlServer;

internal sealed record SchemaMigration(int Version, string Description, string Sql);
