\set ON_ERROR_STOP on

-- This file is ordered after the document and pgvector schemas.
-- The application remains synchronous until a background worker is added.
CREATE TABLE IF NOT EXISTS document_ingestion_jobs
(
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    document_id UUID NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
    status TEXT NOT NULL DEFAULT 'Pending',
    attempt_count INTEGER NOT NULL DEFAULT 0,
    max_attempts INTEGER NOT NULL DEFAULT 3,
    available_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    started_at TIMESTAMPTZ NULL,
    completed_at TIMESTAMPTZ NULL,
    failed_at TIMESTAMPTZ NULL,
    last_error_code VARCHAR(100) NULL,
    last_error_summary VARCHAR(500) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_document_ingestion_jobs_status
        CHECK (status IN ('Pending', 'Processing', 'Completed', 'Failed')),
    CONSTRAINT ck_document_ingestion_jobs_attempts
        CHECK (attempt_count >= 0 AND max_attempts > 0 AND attempt_count <= max_attempts),
    CONSTRAINT ck_document_ingestion_jobs_error_code
        CHECK (last_error_code IS NULL OR length(btrim(last_error_code)) > 0),
    CONSTRAINT ck_document_ingestion_jobs_error_summary
        CHECK (last_error_summary IS NULL OR length(btrim(last_error_summary)) > 0),
    CONSTRAINT ck_document_ingestion_jobs_lifecycle
        CHECK
        (
            (status = 'Pending'
                AND started_at IS NULL
                AND completed_at IS NULL
                AND failed_at IS NULL)
            OR
            (status = 'Processing'
                AND started_at IS NOT NULL
                AND completed_at IS NULL
                AND failed_at IS NULL)
            OR
            (status = 'Completed'
                AND started_at IS NOT NULL
                AND completed_at IS NOT NULL
                AND failed_at IS NULL)
            OR
            (status = 'Failed'
                AND completed_at IS NULL
                AND failed_at IS NOT NULL)
        )
);

-- A document may have historical completed or failed jobs, but only one active job.
CREATE UNIQUE INDEX IF NOT EXISTS ux_document_ingestion_jobs_active_document
    ON document_ingestion_jobs (document_id)
    WHERE status IN ('Pending', 'Processing');

-- Supports ordered worker claiming without scanning completed history.
CREATE INDEX IF NOT EXISTS ix_document_ingestion_jobs_claim
    ON document_ingestion_jobs (available_at, created_at, id)
    WHERE status = 'Pending';

CREATE INDEX IF NOT EXISTS ix_document_ingestion_jobs_document_history
    ON document_ingestion_jobs (document_id, created_at DESC, id DESC);
