\set ON_ERROR_STOP on

\getenv app_db_password APP_DB_PASSWORD
\getenv privileged_db_password PRIVILEGED_DB_PASSWORD

DO
$$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'document_app') THEN
        CREATE ROLE document_app
            LOGIN
            NOSUPERUSER
            NOCREATEDB
            NOCREATEROLE
            NOINHERIT
            NOBYPASSRLS;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'document_privileged') THEN
        CREATE ROLE document_privileged
            LOGIN
            NOSUPERUSER
            NOCREATEDB
            NOCREATEROLE
            NOINHERIT
            NOBYPASSRLS;
    END IF;
END
$$;

SELECT format('ALTER ROLE document_app PASSWORD %L', :'app_db_password')
\gexec
SELECT format('ALTER ROLE document_privileged PASSWORD %L', :'privileged_db_password')
\gexec

ALTER TABLE documents
    ADD COLUMN IF NOT EXISTS tenant_id TEXT;

UPDATE documents
SET tenant_id = 'legacy-tenant'
WHERE tenant_id IS NULL
   OR length(btrim(tenant_id)) = 0;

ALTER TABLE documents
    ALTER COLUMN tenant_id SET DEFAULT 'legacy-tenant';

ALTER TABLE documents
    ALTER COLUMN tenant_id SET NOT NULL;

DO
$$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_documents_tenant_id_not_blank'
          AND conrelid = 'documents'::regclass
    ) THEN
        ALTER TABLE documents
            ADD CONSTRAINT ck_documents_tenant_id_not_blank
            CHECK (length(btrim(tenant_id)) > 0);
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_tenant_id_id
    ON documents (tenant_id, id);

CREATE INDEX IF NOT EXISTS ix_documents_tenant_owner_created_at
    ON documents (tenant_id, owner_id, created_at DESC);

ALTER TABLE document_chunks
    ADD COLUMN IF NOT EXISTS tenant_id TEXT;

UPDATE document_chunks AS chunks
SET tenant_id = documents.tenant_id
FROM documents
WHERE documents.id = chunks.document_id
  AND (chunks.tenant_id IS NULL OR length(btrim(chunks.tenant_id)) = 0);

ALTER TABLE document_chunks
    ALTER COLUMN tenant_id SET NOT NULL;

DO
$$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_document_chunks_tenant_id_not_blank'
          AND conrelid = 'document_chunks'::regclass
    ) THEN
        ALTER TABLE document_chunks
            ADD CONSTRAINT ck_document_chunks_tenant_id_not_blank
            CHECK (length(btrim(tenant_id)) > 0);
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_document_chunks_tenant_document'
          AND conrelid = 'document_chunks'::regclass
    ) THEN
        ALTER TABLE document_chunks
            ADD CONSTRAINT fk_document_chunks_tenant_document
            FOREIGN KEY (tenant_id, document_id)
            REFERENCES documents (tenant_id, id)
            ON DELETE CASCADE;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS ix_document_chunks_tenant_document
    ON document_chunks (tenant_id, document_id, chunk_index);

ALTER TABLE document_ingestion_jobs
    ADD COLUMN IF NOT EXISTS tenant_id TEXT;

UPDATE document_ingestion_jobs AS jobs
SET tenant_id = documents.tenant_id
FROM documents
WHERE documents.id = jobs.document_id
  AND (jobs.tenant_id IS NULL OR length(btrim(jobs.tenant_id)) = 0);

ALTER TABLE document_ingestion_jobs
    ALTER COLUMN tenant_id SET NOT NULL;

DO
$$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_document_ingestion_jobs_tenant_id_not_blank'
          AND conrelid = 'document_ingestion_jobs'::regclass
    ) THEN
        ALTER TABLE document_ingestion_jobs
            ADD CONSTRAINT ck_document_ingestion_jobs_tenant_id_not_blank
            CHECK (length(btrim(tenant_id)) > 0);
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_document_ingestion_jobs_tenant_document'
          AND conrelid = 'document_ingestion_jobs'::regclass
    ) THEN
        ALTER TABLE document_ingestion_jobs
            ADD CONSTRAINT fk_document_ingestion_jobs_tenant_document
            FOREIGN KEY (tenant_id, document_id)
            REFERENCES documents (tenant_id, id)
            ON DELETE CASCADE;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS ix_document_ingestion_jobs_tenant_status
    ON document_ingestion_jobs (tenant_id, status, available_at, id);

GRANT CONNECT ON DATABASE documents TO document_app, document_privileged;
GRANT USAGE ON SCHEMA public TO document_app, document_privileged;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO document_app, document_privileged;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO document_app, document_privileged;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO document_app, document_privileged;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO document_app, document_privileged;

ALTER TABLE documents ENABLE ROW LEVEL SECURITY;
ALTER TABLE documents FORCE ROW LEVEL SECURITY;
ALTER TABLE document_chunks ENABLE ROW LEVEL SECURITY;
ALTER TABLE document_chunks FORCE ROW LEVEL SECURITY;
ALTER TABLE document_ingestion_jobs ENABLE ROW LEVEL SECURITY;
ALTER TABLE document_ingestion_jobs FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_documents ON documents;
CREATE POLICY tenant_documents
    ON documents
    FOR ALL
    TO document_app
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

DROP POLICY IF EXISTS privileged_documents ON documents;
CREATE POLICY privileged_documents
    ON documents
    FOR ALL
    TO document_privileged
    USING (true)
    WITH CHECK (true);

DROP POLICY IF EXISTS tenant_document_chunks ON document_chunks;
CREATE POLICY tenant_document_chunks
    ON document_chunks
    FOR ALL
    TO document_app
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

DROP POLICY IF EXISTS privileged_document_chunks ON document_chunks;
CREATE POLICY privileged_document_chunks
    ON document_chunks
    FOR ALL
    TO document_privileged
    USING (true)
    WITH CHECK (true);

DROP POLICY IF EXISTS tenant_document_ingestion_jobs ON document_ingestion_jobs;
CREATE POLICY tenant_document_ingestion_jobs
    ON document_ingestion_jobs
    FOR ALL
    TO document_app
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

DROP POLICY IF EXISTS privileged_document_ingestion_jobs ON document_ingestion_jobs;
CREATE POLICY privileged_document_ingestion_jobs
    ON document_ingestion_jobs
    FOR ALL
    TO document_privileged
    USING (true)
    WITH CHECK (true);
