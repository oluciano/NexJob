namespace NexJob.Postgres;

internal sealed record SchemaMigration(int Version, string Description, string Sql);
