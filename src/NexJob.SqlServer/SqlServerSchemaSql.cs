namespace NexJob.SqlServer;

internal static class SqlServerSchemaSql
{
    internal const string CreateTables =
        """
        IF OBJECT_ID('nexjob_jobs', 'U') IS NULL
        CREATE TABLE nexjob_jobs (
            id                    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            job_type              NVARCHAR(MAX)    NOT NULL,
            input_type            NVARCHAR(MAX)    NOT NULL,
            input_json            NVARCHAR(MAX)    NOT NULL,
            schema_version        INT              NOT NULL DEFAULT 1,
            queue                 NVARCHAR(200)    NOT NULL DEFAULT 'default',
            priority              INT              NOT NULL DEFAULT 3,
            status                NVARCHAR(50)     NOT NULL DEFAULT 'Enqueued',
            idempotency_key       NVARCHAR(500)    NULL,
            attempts              INT              NOT NULL DEFAULT 0,
            max_attempts          INT              NOT NULL DEFAULT 10,
            created_at            DATETIMEOFFSET   NOT NULL DEFAULT SYSUTCDATETIME(),
            scheduled_at          DATETIMEOFFSET   NULL,
            processing_started_at DATETIMEOFFSET   NULL,
            heartbeat_at          DATETIMEOFFSET   NULL,
            completed_at          DATETIMEOFFSET   NULL,
            retry_at              DATETIMEOFFSET   NULL,
            exception_message     NVARCHAR(MAX)    NULL,
            exception_stack_trace NVARCHAR(MAX)    NULL,
            parent_job_id         UNIQUEIDENTIFIER NULL,
            recurring_job_id      NVARCHAR(500)    NULL,
            execution_logs        NVARCHAR(MAX)    NULL
        );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_nexjob_jobs_fetch' AND object_id = OBJECT_ID('nexjob_jobs'))
        CREATE INDEX idx_nexjob_jobs_fetch ON nexjob_jobs (queue, priority, status, created_at)
            WHERE status IN ('Enqueued', 'Scheduled');

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_nexjob_jobs_heartbeat' AND object_id = OBJECT_ID('nexjob_jobs'))
        CREATE INDEX idx_nexjob_jobs_heartbeat ON nexjob_jobs (status, heartbeat_at)
            WHERE status = 'Processing';

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_nexjob_jobs_parent' AND object_id = OBJECT_ID('nexjob_jobs'))
        CREATE INDEX idx_nexjob_jobs_parent ON nexjob_jobs (parent_job_id)
            WHERE parent_job_id IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_nexjob_jobs_completed' AND object_id = OBJECT_ID('nexjob_jobs'))
        CREATE INDEX idx_nexjob_jobs_completed ON nexjob_jobs (completed_at)
            WHERE completed_at IS NOT NULL;

        IF OBJECT_ID('nexjob_recurring_jobs', 'U') IS NULL
        CREATE TABLE nexjob_recurring_jobs (
            recurring_job_id      NVARCHAR(500)    NOT NULL PRIMARY KEY,
            job_type              NVARCHAR(MAX)    NOT NULL,
            input_type            NVARCHAR(MAX)    NOT NULL,
            input_json            NVARCHAR(MAX)    NOT NULL,
            cron                  NVARCHAR(200)    NOT NULL,
            time_zone_id          NVARCHAR(200)    NOT NULL DEFAULT 'UTC',
            queue                 NVARCHAR(200)    NOT NULL DEFAULT 'default',
            next_execution        DATETIMEOFFSET   NULL,
            last_execution        DATETIMEOFFSET   NULL,
            last_execution_status NVARCHAR(50)     NULL,
            last_execution_error  NVARCHAR(MAX)    NULL,
            concurrency_policy    NVARCHAR(50)     NOT NULL DEFAULT 'SkipIfRunning',
            created_at            DATETIMEOFFSET   NOT NULL DEFAULT SYSUTCDATETIME(),
            updated_at            DATETIMEOFFSET   NOT NULL DEFAULT SYSUTCDATETIME(),
            cron_override         NVARCHAR(200)    NULL,
            enabled               BIT              NOT NULL DEFAULT 1,
            deleted_by_user       BIT              NOT NULL DEFAULT 0
        );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_nexjob_recurring_next' AND object_id = OBJECT_ID('nexjob_recurring_jobs'))
        CREATE INDEX idx_nexjob_recurring_next ON nexjob_recurring_jobs (next_execution);
        """;
}
