CREATE TABLE IF NOT EXISTS documents (
    id UUID PRIMARY KEY,
    file_name VARCHAR(512) NOT NULL,
    content_type VARCHAR(255) NULL,
    size_in_bytes BIGINT NOT NULL,
    storage_path TEXT NOT NULL,
    status VARCHAR(64) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_documents_created_at
    ON documents (created_at DESC);

CREATE INDEX IF NOT EXISTS ix_documents_file_name
    ON documents (file_name);
