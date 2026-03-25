namespace NexJob.Postgres;

internal static class SchemaSQL
{
    internal const string CreateTables =
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
            recurring_job_id  TEXT        PRIMARY KEY,
            job_type          TEXT        NOT NULL,
            input_type        TEXT        NOT NULL,
            input_json        JSONB       NOT NULL,
            cron              TEXT        NOT NULL,
            time_zone_id      TEXT        NOT NULL DEFAULT 'UTC',
            queue             TEXT        NOT NULL DEFAULT 'default',
            next_execution          TIMESTAMPTZ,
            last_execution          TIMESTAMPTZ,
            last_execution_status   TEXT,
            last_execution_error    TEXT,
            concurrency_policy      TEXT        NOT NULL DEFAULT 'SkipIfRunning',
            created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_nexjob_recurring_next
            ON nexjob_recurring_jobs (next_execution);
        """;
}
