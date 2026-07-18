\set ON_ERROR_STOP on

-- This file is ordered after the base schema so the documents table exists
-- before the document_chunks foreign key is created.
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS document_chunks
(
    document_id UUID NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
    chunk_index INTEGER NOT NULL CHECK (chunk_index >= 0),
    content TEXT NOT NULL CHECK (length(btrim(content)) > 0),
    embedding vector(8) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (document_id, chunk_index)
);

CREATE INDEX IF NOT EXISTS ix_document_chunks_embedding_cosine
    ON document_chunks USING hnsw (embedding vector_cosine_ops);
