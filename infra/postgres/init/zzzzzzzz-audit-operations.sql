\set ON_ERROR_STOP on

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

ALTER TABLE audit_events
    ADD COLUMN IF NOT EXISTS chain_sequence BIGINT NULL,
    ADD COLUMN IF NOT EXISTS previous_hash CHAR(64) NULL,
    ADD COLUMN IF NOT EXISTS event_hash CHAR(64) NULL;

CREATE OR REPLACE FUNCTION audit_event_canonical_payload
(
    p_chain_sequence BIGINT,
    p_occurred_at TIMESTAMPTZ,
    p_tenant_id TEXT,
    p_actor_user_id TEXT,
    p_actor_role TEXT,
    p_event_type TEXT,
    p_action TEXT,
    p_resource_type TEXT,
    p_resource_id TEXT,
    p_outcome TEXT,
    p_correlation_id TEXT,
    p_trace_id TEXT,
    p_details JSONB
)
RETURNS TEXT
LANGUAGE SQL
IMMUTABLE
PARALLEL SAFE
AS
$$
    SELECT jsonb_build_object(
        'sequence', p_chain_sequence,
        'occurredAtEpochMicros', floor(extract(epoch FROM p_occurred_at) * 1000000)::BIGINT,
        'tenantId', p_tenant_id,
        'actorUserId', p_actor_user_id,
        'actorRole', p_actor_role,
        'eventType', p_event_type,
        'action', p_action,
        'resourceType', p_resource_type,
        'resourceId', p_resource_id,
        'outcome', p_outcome,
        'correlationId', p_correlation_id,
        'traceId', p_trace_id,
        'details', COALESCE(p_details, '{}'::jsonb)
    )::TEXT;
$$;

CREATE OR REPLACE FUNCTION audit_event_compute_hash
(
    p_previous_hash TEXT,
    p_chain_sequence BIGINT,
    p_occurred_at TIMESTAMPTZ,
    p_tenant_id TEXT,
    p_actor_user_id TEXT,
    p_actor_role TEXT,
    p_event_type TEXT,
    p_action TEXT,
    p_resource_type TEXT,
    p_resource_id TEXT,
    p_outcome TEXT,
    p_correlation_id TEXT,
    p_trace_id TEXT,
    p_details JSONB
)
RETURNS CHAR(64)
LANGUAGE SQL
IMMUTABLE
PARALLEL SAFE
AS
$$
    SELECT encode(
        digest(
            convert_to(
                COALESCE(p_previous_hash, repeat('0', 64)) || '|' ||
                audit_event_canonical_payload(
                    p_chain_sequence,
                    p_occurred_at,
                    p_tenant_id,
                    p_actor_user_id,
                    p_actor_role,
                    p_event_type,
                    p_action,
                    p_resource_type,
                    p_resource_id,
                    p_outcome,
                    p_correlation_id,
                    p_trace_id,
                    p_details),
                'UTF8'),
            'sha256'),
        'hex')::CHAR(64);
$$;

CREATE TABLE IF NOT EXISTS audit_chain_heads
(
    tenant_id TEXT PRIMARY KEY,
    last_sequence BIGINT NOT NULL,
    last_hash CHAR(64) NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_audit_chain_heads_tenant_not_blank CHECK (length(btrim(tenant_id)) > 0),
    CONSTRAINT ck_audit_chain_heads_sequence_nonnegative CHECK (last_sequence >= 0),
    CONSTRAINT ck_audit_chain_heads_hash CHECK (last_hash ~ '^[0-9a-f]{64}$')
);

DO
$$
DECLARE
    tenant_row RECORD;
    audit_row RECORD;
    v_sequence BIGINT;
    v_previous_hash CHAR(64);
    v_hash CHAR(64);
BEGIN
    IF EXISTS (SELECT 1 FROM audit_events WHERE chain_sequence IS NULL) THEN
        FOR tenant_row IN
            SELECT DISTINCT tenant_id
            FROM audit_events
            ORDER BY tenant_id
        LOOP
            v_sequence := 0;
            v_previous_hash := repeat('0', 64)::CHAR(64);

            FOR audit_row IN
                SELECT id, occurred_at, tenant_id, actor_user_id, actor_role,
                       event_type, action, resource_type, resource_id, outcome,
                       correlation_id, trace_id, details
                FROM audit_events
                WHERE tenant_id = tenant_row.tenant_id
                ORDER BY occurred_at, id
            LOOP
                v_sequence := v_sequence + 1;
                v_hash := audit_event_compute_hash(
                    v_previous_hash,
                    v_sequence,
                    audit_row.occurred_at,
                    audit_row.tenant_id,
                    audit_row.actor_user_id,
                    audit_row.actor_role,
                    audit_row.event_type,
                    audit_row.action,
                    audit_row.resource_type,
                    audit_row.resource_id,
                    audit_row.outcome,
                    audit_row.correlation_id,
                    audit_row.trace_id,
                    audit_row.details);

                UPDATE audit_events
                SET chain_sequence = v_sequence,
                    previous_hash = v_previous_hash,
                    event_hash = v_hash
                WHERE id = audit_row.id;

                v_previous_hash := v_hash;
            END LOOP;

            INSERT INTO audit_chain_heads (tenant_id, last_sequence, last_hash, updated_at)
            VALUES (tenant_row.tenant_id, v_sequence, v_previous_hash, CURRENT_TIMESTAMP)
            ON CONFLICT (tenant_id)
            DO UPDATE SET last_sequence = EXCLUDED.last_sequence,
                          last_hash = EXCLUDED.last_hash,
                          updated_at = CURRENT_TIMESTAMP;
        END LOOP;
    END IF;
END
$$;

ALTER TABLE audit_events
    ALTER COLUMN chain_sequence SET NOT NULL,
    ALTER COLUMN previous_hash SET NOT NULL,
    ALTER COLUMN event_hash SET NOT NULL;

DO
$$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_audit_events_chain_sequence_positive') THEN
        ALTER TABLE audit_events
            ADD CONSTRAINT ck_audit_events_chain_sequence_positive CHECK (chain_sequence > 0);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_audit_events_previous_hash') THEN
        ALTER TABLE audit_events
            ADD CONSTRAINT ck_audit_events_previous_hash CHECK (previous_hash ~ '^[0-9a-f]{64}$');
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_audit_events_event_hash') THEN
        ALTER TABLE audit_events
            ADD CONSTRAINT ck_audit_events_event_hash CHECK (event_hash ~ '^[0-9a-f]{64}$');
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_audit_events_tenant_chain_sequence
    ON audit_events (tenant_id, chain_sequence);

CREATE TABLE IF NOT EXISTS audit_event_archive
(
    id BIGINT PRIMARY KEY,
    occurred_at TIMESTAMPTZ NOT NULL,
    tenant_id TEXT NOT NULL,
    actor_user_id TEXT NOT NULL,
    actor_role VARCHAR(50) NOT NULL,
    event_type VARCHAR(100) NOT NULL,
    action VARCHAR(100) NOT NULL,
    resource_type VARCHAR(100) NOT NULL,
    resource_id TEXT NULL,
    outcome VARCHAR(30) NOT NULL,
    correlation_id VARCHAR(128) NOT NULL,
    trace_id VARCHAR(64) NULL,
    details JSONB NOT NULL,
    chain_sequence BIGINT NOT NULL,
    previous_hash CHAR(64) NOT NULL,
    event_hash CHAR(64) NOT NULL,
    archived_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_audit_event_archive_tenant_not_blank CHECK (length(btrim(tenant_id)) > 0),
    CONSTRAINT ck_audit_event_archive_actor_not_blank CHECK (length(btrim(actor_user_id)) > 0),
    CONSTRAINT ck_audit_event_archive_event_type_not_blank CHECK (length(btrim(event_type)) > 0),
    CONSTRAINT ck_audit_event_archive_outcome CHECK (outcome IN ('success', 'failure', 'not_found', 'denied')),
    CONSTRAINT ck_audit_event_archive_chain_sequence_positive CHECK (chain_sequence > 0),
    CONSTRAINT ck_audit_event_archive_previous_hash CHECK (previous_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_audit_event_archive_event_hash CHECK (event_hash ~ '^[0-9a-f]{64}$')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_audit_event_archive_tenant_chain_sequence
    ON audit_event_archive (tenant_id, chain_sequence);
CREATE INDEX IF NOT EXISTS ix_audit_event_archive_tenant_occurred_at
    ON audit_event_archive (tenant_id, occurred_at DESC, id DESC);
CREATE INDEX IF NOT EXISTS ix_audit_event_archive_archived_at
    ON audit_event_archive (archived_at DESC, id DESC);

CREATE OR REPLACE FUNCTION assign_audit_event_chain()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS
$$
DECLARE
    v_last_sequence BIGINT;
    v_last_hash CHAR(64);
BEGIN
    IF NEW.tenant_id IS NULL OR length(btrim(NEW.tenant_id)) = 0 THEN
        RAISE EXCEPTION 'audit tenant_id is required' USING ERRCODE = '23514';
    END IF;

    PERFORM pg_advisory_xact_lock(hashtextextended(NEW.tenant_id, 0));

    SELECT last_sequence, last_hash
    INTO v_last_sequence, v_last_hash
    FROM audit_chain_heads
    WHERE tenant_id = NEW.tenant_id
    FOR UPDATE;

    IF NOT FOUND THEN
        v_last_sequence := 0;
        v_last_hash := repeat('0', 64)::CHAR(64);
        INSERT INTO audit_chain_heads (tenant_id, last_sequence, last_hash, updated_at)
        VALUES (NEW.tenant_id, 0, v_last_hash, CURRENT_TIMESTAMP)
        ON CONFLICT (tenant_id) DO NOTHING;

        SELECT last_sequence, last_hash
        INTO v_last_sequence, v_last_hash
        FROM audit_chain_heads
        WHERE tenant_id = NEW.tenant_id
        FOR UPDATE;
    END IF;

    NEW.chain_sequence := v_last_sequence + 1;
    NEW.previous_hash := v_last_hash;
    NEW.event_hash := audit_event_compute_hash(
        NEW.previous_hash,
        NEW.chain_sequence,
        NEW.occurred_at,
        NEW.tenant_id,
        NEW.actor_user_id,
        NEW.actor_role,
        NEW.event_type,
        NEW.action,
        NEW.resource_type,
        NEW.resource_id,
        NEW.outcome,
        NEW.correlation_id,
        NEW.trace_id,
        NEW.details);

    UPDATE audit_chain_heads
    SET last_sequence = NEW.chain_sequence,
        last_hash = NEW.event_hash,
        updated_at = CURRENT_TIMESTAMP
    WHERE tenant_id = NEW.tenant_id;

    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS trg_assign_audit_event_chain ON audit_events;
CREATE TRIGGER trg_assign_audit_event_chain
    BEFORE INSERT ON audit_events
    FOR EACH ROW
    EXECUTE FUNCTION assign_audit_event_chain();

CREATE OR REPLACE FUNCTION verify_audit_chain(p_tenant_id TEXT)
RETURNS TABLE
(
    is_valid BOOLEAN,
    checked_count BIGINT,
    first_broken_sequence BIGINT,
    head_sequence BIGINT
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS
$$
DECLARE
    audit_row RECORD;
    v_expected_sequence BIGINT := 0;
    v_previous_hash CHAR(64) := repeat('0', 64)::CHAR(64);
    v_expected_hash CHAR(64);
    v_head_sequence BIGINT := 0;
    v_head_hash CHAR(64) := repeat('0', 64)::CHAR(64);
    v_tenant_context TEXT;
BEGIN
    IF p_tenant_id IS NULL OR length(btrim(p_tenant_id)) = 0 THEN
        RAISE EXCEPTION 'tenant id is required' USING ERRCODE = '22023';
    END IF;

    IF session_user = 'document_app' THEN
        v_tenant_context := NULLIF(current_setting('app.tenant_id', true), '');
        IF v_tenant_context IS NULL OR v_tenant_context <> p_tenant_id THEN
            RAISE EXCEPTION 'audit chain verification is outside the tenant context'
                USING ERRCODE = '42501';
        END IF;
    END IF;

    SELECT last_sequence, last_hash
    INTO v_head_sequence, v_head_hash
    FROM audit_chain_heads
    WHERE tenant_id = p_tenant_id;

    IF NOT FOUND THEN
        v_head_sequence := 0;
        v_head_hash := repeat('0', 64)::CHAR(64);
    END IF;

    FOR audit_row IN
        SELECT occurred_at, tenant_id, actor_user_id, actor_role,
               event_type, action, resource_type, resource_id, outcome,
               correlation_id, trace_id, details, chain_sequence,
               previous_hash, event_hash
        FROM
        (
            SELECT occurred_at, tenant_id, actor_user_id, actor_role,
                   event_type, action, resource_type, resource_id, outcome,
                   correlation_id, trace_id, details, chain_sequence,
                   previous_hash, event_hash
            FROM audit_event_archive
            WHERE tenant_id = p_tenant_id
            UNION ALL
            SELECT occurred_at, tenant_id, actor_user_id, actor_role,
                   event_type, action, resource_type, resource_id, outcome,
                   correlation_id, trace_id, details, chain_sequence,
                   previous_hash, event_hash
            FROM audit_events
            WHERE tenant_id = p_tenant_id
        ) AS combined
        ORDER BY chain_sequence
    LOOP
        v_expected_sequence := v_expected_sequence + 1;
        v_expected_hash := audit_event_compute_hash(
            v_previous_hash,
            v_expected_sequence,
            audit_row.occurred_at,
            audit_row.tenant_id,
            audit_row.actor_user_id,
            audit_row.actor_role,
            audit_row.event_type,
            audit_row.action,
            audit_row.resource_type,
            audit_row.resource_id,
            audit_row.outcome,
            audit_row.correlation_id,
            audit_row.trace_id,
            audit_row.details);

        IF audit_row.chain_sequence <> v_expected_sequence
           OR audit_row.previous_hash <> v_previous_hash
           OR audit_row.event_hash <> v_expected_hash THEN
            RETURN QUERY SELECT FALSE, v_expected_sequence - 1, v_expected_sequence, v_head_sequence;
            RETURN;
        END IF;

        v_previous_hash := audit_row.event_hash;
    END LOOP;

    IF v_head_sequence <> v_expected_sequence OR v_head_hash <> v_previous_hash THEN
        RETURN QUERY SELECT FALSE, v_expected_sequence, v_expected_sequence + 1, v_head_sequence;
        RETURN;
    END IF;

    RETURN QUERY SELECT TRUE, v_expected_sequence, NULL::BIGINT, v_head_sequence;
END
$$;

CREATE OR REPLACE FUNCTION archive_audit_events
(
    p_cutoff TIMESTAMPTZ,
    p_batch_size INTEGER
)
RETURNS INTEGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS
$$
DECLARE
    v_archived INTEGER;
BEGIN
    IF p_cutoff IS NULL OR p_cutoff >= CURRENT_TIMESTAMP THEN
        RAISE EXCEPTION 'audit archive cutoff must be in the past' USING ERRCODE = '22023';
    END IF;

    IF p_batch_size IS NULL OR p_batch_size < 1 OR p_batch_size > 10000 THEN
        RAISE EXCEPTION 'audit archive batch size must be between 1 and 10000' USING ERRCODE = '22023';
    END IF;

    WITH candidates AS
    (
        SELECT id
        FROM audit_events
        WHERE occurred_at < p_cutoff
        ORDER BY occurred_at, id
        LIMIT p_batch_size
        FOR UPDATE SKIP LOCKED
    ),
    moved AS
    (
        DELETE FROM audit_events AS active
        USING candidates
        WHERE active.id = candidates.id
        RETURNING active.*
    ),
    archived AS
    (
        INSERT INTO audit_event_archive
            (id, occurred_at, tenant_id, actor_user_id, actor_role, event_type,
             action, resource_type, resource_id, outcome, correlation_id, trace_id,
             details, chain_sequence, previous_hash, event_hash, archived_at)
        SELECT id, occurred_at, tenant_id, actor_user_id, actor_role, event_type,
               action, resource_type, resource_id, outcome, correlation_id, trace_id,
               details, chain_sequence, previous_hash, event_hash, CURRENT_TIMESTAMP
        FROM moved
        RETURNING 1
    )
    SELECT count(*)::INTEGER INTO v_archived FROM archived;

    RETURN v_archived;
END
$$;

REVOKE ALL ON audit_chain_heads FROM PUBLIC, document_app, document_platform, document_privileged;
REVOKE ALL ON audit_event_archive FROM PUBLIC, document_app, document_platform, document_privileged;
REVOKE UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER
    ON audit_events
    FROM document_app, document_platform, document_privileged;

GRANT SELECT, INSERT ON audit_events TO document_app, document_platform, document_privileged;
GRANT SELECT ON audit_event_archive TO document_app, document_platform, document_privileged;

ALTER TABLE audit_event_archive ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_event_archive FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_audit_event_archive_select ON audit_event_archive;
CREATE POLICY tenant_audit_event_archive_select
    ON audit_event_archive
    FOR SELECT
    TO document_app
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

DROP POLICY IF EXISTS platform_audit_event_archive_select ON audit_event_archive;
CREATE POLICY platform_audit_event_archive_select
    ON audit_event_archive
    FOR SELECT
    TO document_platform
    USING (true);

DROP POLICY IF EXISTS privileged_audit_event_archive_select ON audit_event_archive;
CREATE POLICY privileged_audit_event_archive_select
    ON audit_event_archive
    FOR SELECT
    TO document_privileged
    USING (true);

REVOKE ALL ON FUNCTION audit_event_canonical_payload(
    BIGINT, TIMESTAMPTZ, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, JSONB)
    FROM PUBLIC;
REVOKE ALL ON FUNCTION audit_event_compute_hash(
    TEXT, BIGINT, TIMESTAMPTZ, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, TEXT, JSONB)
    FROM PUBLIC;
REVOKE ALL ON FUNCTION verify_audit_chain(TEXT) FROM PUBLIC;
REVOKE ALL ON FUNCTION archive_audit_events(TIMESTAMPTZ, INTEGER) FROM PUBLIC;

GRANT EXECUTE ON FUNCTION verify_audit_chain(TEXT)
    TO document_app, document_platform, document_privileged;
GRANT EXECUTE ON FUNCTION archive_audit_events(TIMESTAMPTZ, INTEGER)
    TO document_privileged;

COMMIT;
