namespace NexJob.Postgres;

internal static class SchemaSql
{
    /// <summary>V1: Initial schema — nexjob_jobs table, indexes, nexjob_recurring_jobs table.</summary>
    internal const string V1CreateTables =
        """
        CREATE TABLE IF NOT EXISTS nexjob_jobs (
            id                    UUID         PRIMARY KEY,
            job_type              TEXT         NOT NULL,
            input_type            TEXT         NOT NULL,
            input_json            JSONB        NOT NULL,
            schema_version        INT          NOT NULL DEFAULT 1,
            queue                 TEXT         NOT NULL DEFAULT 'default',
            priority              INT          NOT NULL DEFAULT 3,
            status                TEXT         NOT NULL DEFAULT 'Enqueued',
            idempotency_key       TEXT,
            attempts              INT          NOT NULL DEFAULT 0,
            max_attempts          INT          NOT NULL DEFAULT 10,
            created_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            scheduled_at          TIMESTAMPTZ,
            processing_started_at TIMESTAMPTZ,
            heartbeat_at          TIMESTAMPTZ,
            completed_at          TIMESTAMPTZ,
            retry_at              TIMESTAMPTZ,
            exception_message     TEXT,
            exception_stack_trace TEXT,
            parent_job_id         UUID,
            recurring_job_id      TEXT
        );

        CREATE INDEX IF NOT EXISTS idx_nexjob_jobs_fetch
            ON nexjob_jobs (queue, priority, status, created_at)
            WHERE status IN ('Enqueued','Scheduled');

        CREATE INDEX IF NOT EXISTS idx_nexjob_jobs_heartbeat
            ON nexjob_jobs (status, heartbeat_at)
            WHERE status = 'Processing';

        CREATE INDEX IF NOT EXISTS idx_nexjob_jobs_parent
            ON nexjob_jobs (parent_job_id)
            WHERE parent_job_id IS NOT NULL;

        CREATE INDEX IF NOT EXISTS idx_nexjob_jobs_completed
            ON nexjob_jobs (completed_at)
            WHERE completed_at IS NOT NULL;

        CREATE TABLE IF NOT EXISTS nexjob_recurring_jobs (
            recurring_job_id      TEXT        PRIMARY KEY,
            job_type              TEXT        NOT NULL,
            input_type            TEXT        NOT NULL,
            input_json            JSONB       NOT NULL,
            cron                  TEXT        NOT NULL,
            time_zone_id          TEXT        NOT NULL DEFAULT 'UTC',
            queue                 TEXT        NOT NULL DEFAULT 'default',
            next_execution        TIMESTAMPTZ,
            last_execution        TIMESTAMPTZ,
            last_execution_status TEXT,
            last_execution_error  TEXT,
            concurrency_policy    TEXT        NOT NULL DEFAULT 'SkipIfRunning',
            created_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at            TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_nexjob_recurring_next
            ON nexjob_recurring_jobs (next_execution);
        """;

    /// <summary>V2: Add cron_override, enabled, and deleted_by_user columns to nexjob_recurring_jobs.</summary>
    internal const string V2AlterRecurring =
        """
        ALTER TABLE nexjob_recurring_jobs ADD COLUMN IF NOT EXISTS cron_override TEXT NULL;
        ALTER TABLE nexjob_recurring_jobs ADD COLUMN IF NOT EXISTS enabled BOOLEAN NOT NULL DEFAULT TRUE;
        ALTER TABLE nexjob_recurring_jobs ADD COLUMN IF NOT EXISTS deleted_by_user BOOLEAN NOT NULL DEFAULT FALSE;
        """;

    /// <summary>V3: Add execution_logs and trace_parent columns to nexjob_jobs.</summary>
    internal const string V3AddColumns =
        """
        ALTER TABLE nexjob_jobs ADD COLUMN IF NOT EXISTS execution_logs JSONB NULL;
        ALTER TABLE nexjob_jobs ADD COLUMN IF NOT EXISTS trace_parent TEXT NULL;
        """;

    /// <summary>V4: Create nexjob_schema_version and nexjob_recurring_locks tables.</summary>
    internal const string V4CreateVersionTable =
        """
        CREATE TABLE IF NOT EXISTS nexjob_schema_version (
            version     INT         PRIMARY KEY,
            applied_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            description TEXT        NOT NULL
        );
        CREATE TABLE IF NOT EXISTS nexjob_recurring_locks (
            recurring_job_id TEXT        PRIMARY KEY,
            expires_at       TIMESTAMPTZ NOT NULL
        );
        """;

    /// <summary>V5: Add progress_percent, progress_message, and tags columns to nexjob_jobs.</summary>
    internal const string V5AddProgressAndTags =
        """
        ALTER TABLE nexjob_jobs ADD COLUMN IF NOT EXISTS progress_percent   INT   NULL;
        ALTER TABLE nexjob_jobs ADD COLUMN IF NOT EXISTS progress_message   TEXT  NULL;
        ALTER TABLE nexjob_jobs ADD COLUMN IF NOT EXISTS tags               TEXT[] NOT NULL DEFAULT '{}';
        """;

    /// <summary>V6: Create nexjob_servers table for active worker node tracking.</summary>
    internal const string V6CreateServersTable =
        """
        CREATE TABLE IF NOT EXISTS nexjob_servers (
            id             TEXT        PRIMARY KEY,
            worker_count   INT         NOT NULL,
            queues         TEXT[]      NOT NULL,
            started_at     TIMESTAMPTZ NOT NULL,
            heartbeat_at   TIMESTAMPTZ NOT NULL
        );
        """;

    /// <summary>V7: Create nexjob_settings table for persistent runtime configuration.</summary>
    internal const string V7CreateSettingsTable =
        """
        CREATE TABLE IF NOT EXISTS nexjob_settings (
            key        TEXT        PRIMARY KEY,
            value      TEXT        NOT NULL,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    /// <summary>V8: Add unique index for idempotency_key to prevent concurrent duplicates.</summary>
    internal const string V8AddIdempotencyKeyIndex =
        """
        CREATE UNIQUE INDEX IF NOT EXISTS ux_nexjob_jobs_idempotency_key ON nexjob_jobs (idempotency_key)
            WHERE idempotency_key IS NOT NULL;
        """;

    /// <summary>Full initial schema — kept for backward compatibility. Prefer the versioned consts.</summary>
    internal const string CreateTables = V1CreateTables;
}
