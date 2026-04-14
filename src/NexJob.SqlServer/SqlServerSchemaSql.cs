namespace NexJob.SqlServer;

internal static class SqlServerSchemaSql
{
    /// <summary>V1: Initial schema — nexjob_jobs table (including execution_logs), indexes, nexjob_recurring_jobs table.</summary>
    internal const string V1CreateTables =
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
            updated_at            DATETIMEOFFSET   NOT NULL DEFAULT SYSUTCDATETIME()
        );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_nexjob_recurring_next' AND object_id = OBJECT_ID('nexjob_recurring_jobs'))
        CREATE INDEX idx_nexjob_recurring_next ON nexjob_recurring_jobs (next_execution);
        """;

    /// <summary>V2: Add cron_override, enabled, and deleted_by_user columns to nexjob_recurring_jobs.</summary>
    internal const string V2AlterRecurring =
        """
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('nexjob_recurring_jobs') AND name = 'cron_override')
            ALTER TABLE nexjob_recurring_jobs ADD cron_override NVARCHAR(200) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('nexjob_recurring_jobs') AND name = 'enabled')
            ALTER TABLE nexjob_recurring_jobs ADD enabled BIT NOT NULL DEFAULT 1;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('nexjob_recurring_jobs') AND name = 'deleted_by_user')
            ALTER TABLE nexjob_recurring_jobs ADD deleted_by_user BIT NOT NULL DEFAULT 0;
        """;

    /// <summary>V3: Add trace_parent column to nexjob_jobs.</summary>
    internal const string V3AddColumns =
        """
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('nexjob_jobs') AND name = 'trace_parent')
            ALTER TABLE nexjob_jobs ADD trace_parent NVARCHAR(500) NULL;
        """;

    /// <summary>V4: Create nexjob_schema_version and nexjob_recurring_locks tables.</summary>
    internal const string V4CreateVersionTable =
        """
        IF OBJECT_ID('nexjob_schema_version', 'U') IS NULL
        CREATE TABLE nexjob_schema_version (
            version     INT           PRIMARY KEY,
            applied_at  DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
            description NVARCHAR(500) NOT NULL
        );
        IF OBJECT_ID('nexjob_recurring_locks', 'U') IS NULL
        CREATE TABLE nexjob_recurring_locks (
            recurring_job_id NVARCHAR(500) PRIMARY KEY,
            expires_at       DATETIME2     NOT NULL
        );
        """;

    /// <summary>V5: Add progress_percent, progress_message, and tags columns to nexjob_jobs.</summary>
    internal const string V5AddProgressAndTags =
        """
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('nexjob_jobs') AND name = 'progress_percent')
            ALTER TABLE nexjob_jobs ADD progress_percent INT NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('nexjob_jobs') AND name = 'progress_message')
            ALTER TABLE nexjob_jobs ADD progress_message NVARCHAR(MAX) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('nexjob_jobs') AND name = 'tags')
            ALTER TABLE nexjob_jobs ADD tags NVARCHAR(MAX) NOT NULL DEFAULT '[]';
        """;

    /// <summary>V6: Add nexjob_servers table for active server tracking.</summary>
    internal const string V6CreateServersTable =
        """
        IF OBJECT_ID('nexjob_servers', 'U') IS NULL
        CREATE TABLE nexjob_servers (
            id            NVARCHAR(500) NOT NULL PRIMARY KEY,
            worker_count  INT NOT NULL DEFAULT 1,
            queues        NVARCHAR(MAX) NOT NULL DEFAULT '[]',
            started_at    DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),
            heartbeat_at  DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()
        );

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'idx_nexjob_servers_heartbeat' AND object_id = OBJECT_ID('nexjob_servers'))
        CREATE INDEX idx_nexjob_servers_heartbeat ON nexjob_servers (heartbeat_at)
            WHERE heartbeat_at IS NOT NULL;
        """;

    /// <summary>V7: Create nexjob_settings table for persistent runtime configuration.</summary>
    internal const string V7CreateSettingsTable =
        """
        IF OBJECT_ID('nexjob_settings', 'U') IS NULL
        CREATE TABLE nexjob_settings (
            [key]      NVARCHAR(200) PRIMARY KEY,
            [value]    NVARCHAR(MAX) NOT NULL,
            updated_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
        );
        """;

    /// <summary>V8: Add unique sparse index for idempotency_key to prevent concurrent duplicates.</summary>
    internal const string V8AddIdempotencyKeyIndex =
        """
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_nexjob_jobs_idempotency_key' AND object_id = OBJECT_ID('nexjob_jobs'))
        CREATE UNIQUE INDEX ux_nexjob_jobs_idempotency_key ON nexjob_jobs (idempotency_key)
            WHERE idempotency_key IS NOT NULL;
        """;

    /// <summary>Full initial schema — kept for backward compatibility. Prefer the versioned consts.</summary>
    internal const string CreateTables = V1CreateTables;
}
