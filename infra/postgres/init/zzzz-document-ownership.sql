\set ON_ERROR_STOP on

ALTER TABLE documents
    ADD COLUMN IF NOT EXISTS owner_id TEXT;

UPDATE documents
SET owner_id = 'legacy-system'
WHERE owner_id IS NULL
   OR length(btrim(owner_id)) = 0;

ALTER TABLE documents
    ALTER COLUMN owner_id SET DEFAULT 'legacy-system';

ALTER TABLE documents
    ALTER COLUMN owner_id SET NOT NULL;

DO
$$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ck_documents_owner_id_not_blank'
          AND conrelid = 'documents'::regclass
    ) THEN
        ALTER TABLE documents
            ADD CONSTRAINT ck_documents_owner_id_not_blank
            CHECK (length(btrim(owner_id)) > 0);
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS ix_documents_owner_created_at
    ON documents (owner_id, created_at DESC);
