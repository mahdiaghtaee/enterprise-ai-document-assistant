\set ON_ERROR_STOP on

\getenv platform_db_password PLATFORM_DB_PASSWORD

DO
$$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'document_platform') THEN
        CREATE ROLE document_platform
            LOGIN
            NOSUPERUSER
            NOCREATEDB
            NOCREATEROLE
            NOINHERIT
            NOBYPASSRLS;
    END IF;
END
$$;

SELECT format('ALTER ROLE document_platform PASSWORD %L', :'platform_db_password')
\gexec

CREATE TABLE IF NOT EXISTS tenants
(
    tenant_id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'Active',
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    disabled_at TIMESTAMPTZ NULL,
    disabled_by TEXT NULL,
    CONSTRAINT ck_tenants_tenant_id_not_blank CHECK (length(btrim(tenant_id)) > 0),
    CONSTRAINT ck_tenants_display_name_not_blank CHECK (length(btrim(display_name)) > 0),
    CONSTRAINT ck_tenants_status CHECK (status IN ('Active', 'Disabled')),
    CONSTRAINT ck_tenants_disabled_state CHECK
    (
        (status = 'Active' AND disabled_at IS NULL AND disabled_by IS NULL)
        OR
        (status = 'Disabled' AND disabled_at IS NOT NULL AND disabled_by IS NOT NULL)
    )
);

CREATE TABLE IF NOT EXISTS tenant_memberships
(
    tenant_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    role TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'Active',
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    removed_at TIMESTAMPTZ NULL,
    removed_by TEXT NULL,
    PRIMARY KEY (tenant_id, user_id),
    CONSTRAINT fk_tenant_memberships_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE CASCADE,
    CONSTRAINT ck_tenant_memberships_user_id_not_blank CHECK (length(btrim(user_id)) > 0),
    CONSTRAINT ck_tenant_memberships_role CHECK (role IN ('User', 'Admin')),
    CONSTRAINT ck_tenant_memberships_status CHECK (status IN ('Active', 'Removed')),
    CONSTRAINT ck_tenant_memberships_removed_state CHECK
    (
        (status = 'Active' AND removed_at IS NULL AND removed_by IS NULL)
        OR
        (status = 'Removed' AND removed_at IS NOT NULL AND removed_by IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_tenant_memberships_tenant_status_role
    ON tenant_memberships (tenant_id, status, role, user_id);

CREATE TABLE IF NOT EXISTS tenant_invitations
(
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    invitee_user_id TEXT NOT NULL,
    role TEXT NOT NULL,
    token_hash CHAR(64) NOT NULL,
    status TEXT NOT NULL DEFAULT 'Pending',
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by TEXT NOT NULL,
    accepted_at TIMESTAMPTZ NULL,
    accepted_by TEXT NULL,
    revoked_at TIMESTAMPTZ NULL,
    revoked_by TEXT NULL,
    CONSTRAINT fk_tenant_invitations_tenant
        FOREIGN KEY (tenant_id) REFERENCES tenants (tenant_id) ON DELETE CASCADE,
    CONSTRAINT ck_tenant_invitations_invitee_not_blank CHECK (length(btrim(invitee_user_id)) > 0),
    CONSTRAINT ck_tenant_invitations_role CHECK (role IN ('User', 'Admin')),
    CONSTRAINT ck_tenant_invitations_token_hash CHECK (token_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_tenant_invitations_status
        CHECK (status IN ('Pending', 'Accepted', 'Revoked', 'Expired')),
    CONSTRAINT ck_tenant_invitations_terminal_state CHECK
    (
        (status IN ('Pending', 'Expired')
            AND accepted_at IS NULL AND accepted_by IS NULL
            AND revoked_at IS NULL AND revoked_by IS NULL)
        OR
        (status = 'Accepted'
            AND accepted_at IS NOT NULL AND accepted_by IS NOT NULL
            AND revoked_at IS NULL AND revoked_by IS NULL)
        OR
        (status = 'Revoked'
            AND revoked_at IS NOT NULL AND revoked_by IS NOT NULL
            AND accepted_at IS NULL AND accepted_by IS NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_invitations_token_hash
    ON tenant_invitations (token_hash);

CREATE UNIQUE INDEX IF NOT EXISTS ux_tenant_invitations_pending_invitee
    ON tenant_invitations (tenant_id, invitee_user_id)
    WHERE status = 'Pending';

CREATE INDEX IF NOT EXISTS ix_tenant_invitations_tenant_status_created
    ON tenant_invitations (tenant_id, status, created_at DESC);

-- Backfill managed tenant records for existing installations.
INSERT INTO tenants
    (tenant_id, display_name, status, created_at, created_by, updated_at)
SELECT DISTINCT
       documents.tenant_id,
       documents.tenant_id,
       'Active',
       CURRENT_TIMESTAMP,
       'migration-system',
       CURRENT_TIMESTAMP
FROM documents
ON CONFLICT (tenant_id) DO NOTHING;

INSERT INTO tenants
    (tenant_id, display_name, status, created_at, created_by, updated_at)
VALUES
    ('legacy-tenant', 'Legacy tenant', 'Active', CURRENT_TIMESTAMP, 'migration-system', CURRENT_TIMESTAMP)
ON CONFLICT (tenant_id) DO NOTHING;

INSERT INTO tenant_memberships
    (tenant_id, user_id, role, status, created_at, created_by, updated_at)
SELECT DISTINCT
       documents.tenant_id,
       documents.owner_id,
       'User',
       'Active',
       CURRENT_TIMESTAMP,
       'migration-system',
       CURRENT_TIMESTAMP
FROM documents
ON CONFLICT (tenant_id, user_id) DO NOTHING;

INSERT INTO tenant_memberships
    (tenant_id, user_id, role, status, created_at, created_by, updated_at)
VALUES
    ('legacy-tenant', 'legacy-system', 'Admin', 'Active', CURRENT_TIMESTAMP, 'migration-system', CURRENT_TIMESTAMP)
ON CONFLICT (tenant_id, user_id)
DO UPDATE SET role = 'Admin', status = 'Active', updated_at = CURRENT_TIMESTAMP,
              removed_at = NULL, removed_by = NULL;

WITH first_owner AS
(
    SELECT tenant_id, MIN(owner_id) AS user_id
    FROM documents
    GROUP BY tenant_id
)
UPDATE tenant_memberships AS memberships
SET role = 'Admin',
    status = 'Active',
    updated_at = CURRENT_TIMESTAMP,
    removed_at = NULL,
    removed_by = NULL
FROM first_owner
WHERE memberships.tenant_id = first_owner.tenant_id
  AND memberships.user_id = first_owner.user_id
  AND NOT EXISTS
  (
      SELECT 1
      FROM tenant_memberships AS existing_admin
      WHERE existing_admin.tenant_id = memberships.tenant_id
        AND existing_admin.role = 'Admin'
        AND existing_admin.status = 'Active'
  );

SELECT format(
    'GRANT CONNECT ON DATABASE %I TO document_platform',
    current_database())
\gexec
GRANT USAGE ON SCHEMA public TO document_platform;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO document_platform;

REVOKE ALL ON tenants, tenant_memberships, tenant_invitations
    FROM document_app, document_privileged, document_platform;

GRANT SELECT ON tenants TO document_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON tenant_memberships, tenant_invitations TO document_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON tenants, tenant_memberships, tenant_invitations
    TO document_privileged, document_platform;

GRANT SELECT ON documents, document_chunks, document_ingestion_jobs TO document_platform;
GRANT SELECT, INSERT ON audit_events TO document_platform;
GRANT USAGE, SELECT ON SEQUENCE audit_events_id_seq TO document_platform;

ALTER TABLE tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenants FORCE ROW LEVEL SECURITY;
ALTER TABLE tenant_memberships ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenant_memberships FORCE ROW LEVEL SECURITY;
ALTER TABLE tenant_invitations ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenant_invitations FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_tenants ON tenants;
CREATE POLICY tenant_tenants
    ON tenants
    FOR SELECT
    TO document_app
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

DROP POLICY IF EXISTS privileged_tenants ON tenants;
CREATE POLICY privileged_tenants
    ON tenants
    FOR ALL
    TO document_privileged
    USING (true)
    WITH CHECK (true);

DROP POLICY IF EXISTS platform_tenants ON tenants;
CREATE POLICY platform_tenants
    ON tenants
    FOR ALL
    TO document_platform
    USING (true)
    WITH CHECK (true);

DROP POLICY IF EXISTS tenant_memberships_policy ON tenant_memberships;
CREATE POLICY tenant_memberships_policy
    ON tenant_memberships
    FOR ALL
    TO document_app
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

DROP POLICY IF EXISTS privileged_memberships ON tenant_memberships;
CREATE POLICY privileged_memberships
    ON tenant_memberships
    FOR ALL
    TO document_privileged
    USING (true)
    WITH CHECK (true);

DROP POLICY IF EXISTS platform_memberships ON tenant_memberships;
CREATE POLICY platform_memberships
    ON tenant_memberships
    FOR ALL
    TO document_platform
    USING (true)
    WITH CHECK (true);

DROP POLICY IF EXISTS tenant_invitations_policy ON tenant_invitations;
CREATE POLICY tenant_invitations_policy
    ON tenant_invitations
    FOR ALL
    TO document_app
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

DROP POLICY IF EXISTS privileged_invitations ON tenant_invitations;
CREATE POLICY privileged_invitations
    ON tenant_invitations
    FOR ALL
    TO document_privileged
    USING (true)
    WITH CHECK (true);

DROP POLICY IF EXISTS platform_invitations ON tenant_invitations;
CREATE POLICY platform_invitations
    ON tenant_invitations
    FOR ALL
    TO document_platform
    USING (true)
    WITH CHECK (true);

-- Narrow cross-tenant read policies for the public PlatformAdmin path.
DROP POLICY IF EXISTS platform_documents ON documents;
CREATE POLICY platform_documents
    ON documents
    FOR SELECT
    TO document_platform
    USING (true);

DROP POLICY IF EXISTS platform_document_chunks ON document_chunks;
CREATE POLICY platform_document_chunks
    ON document_chunks
    FOR SELECT
    TO document_platform
    USING (true);

DROP POLICY IF EXISTS platform_document_ingestion_jobs ON document_ingestion_jobs;
CREATE POLICY platform_document_ingestion_jobs
    ON document_ingestion_jobs
    FOR SELECT
    TO document_platform
    USING (true);

DROP POLICY IF EXISTS platform_audit_events ON audit_events;
CREATE POLICY platform_audit_events
    ON audit_events
    FOR SELECT
    TO document_platform
    USING (true);

DROP POLICY IF EXISTS platform_insert_audit_events ON audit_events;
CREATE POLICY platform_insert_audit_events
    ON audit_events
    FOR INSERT
    TO document_platform
    WITH CHECK (true);
