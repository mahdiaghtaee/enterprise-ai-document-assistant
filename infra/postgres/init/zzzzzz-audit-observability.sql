\set ON_ERROR_STOP on

CREATE TABLE IF NOT EXISTS audit_events
(
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
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
    details JSONB NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT ck_audit_events_tenant_id_not_blank CHECK (length(btrim(tenant_id)) > 0),
    CONSTRAINT ck_audit_events_actor_user_id_not_blank CHECK (length(btrim(actor_user_id)) > 0),
    CONSTRAINT ck_audit_events_event_type_not_blank CHECK (length(btrim(event_type)) > 0),
    CONSTRAINT ck_audit_events_correlation_id_not_blank CHECK (length(btrim(correlation_id)) > 0),
    CONSTRAINT ck_audit_events_outcome CHECK (outcome IN ('success', 'failure', 'not_found', 'denied'))
);

CREATE INDEX IF NOT EXISTS ix_audit_events_tenant_occurred_at
    ON audit_events (tenant_id, occurred_at DESC, id DESC);

CREATE INDEX IF NOT EXISTS ix_audit_events_event_type_occurred_at
    ON audit_events (event_type, occurred_at DESC);

REVOKE UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER
    ON audit_events
    FROM document_app, document_privileged;
GRANT SELECT, INSERT ON audit_events TO document_app, document_privileged;
GRANT USAGE, SELECT ON SEQUENCE audit_events_id_seq TO document_app, document_privileged;

ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_events FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_audit_events_select ON audit_events;
CREATE POLICY tenant_audit_events_select
    ON audit_events
    FOR SELECT
    TO document_app
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

DROP POLICY IF EXISTS tenant_audit_events_insert ON audit_events;
CREATE POLICY tenant_audit_events_insert
    ON audit_events
    FOR INSERT
    TO document_app
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

DROP POLICY IF EXISTS privileged_audit_events_select ON audit_events;
CREATE POLICY privileged_audit_events_select
    ON audit_events
    FOR SELECT
    TO document_privileged
    USING (true);

DROP POLICY IF EXISTS privileged_audit_events_insert ON audit_events;
CREATE POLICY privileged_audit_events_insert
    ON audit_events
    FOR INSERT
    TO document_privileged
    WITH CHECK (true);

CREATE OR REPLACE FUNCTION audit_document_change()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS
$$
DECLARE
    audit_event_type TEXT;
    audit_action TEXT;
    audit_actor TEXT;
    audit_role TEXT;
    audit_details JSONB;
BEGIN
    IF TG_OP = 'INSERT' THEN
        audit_event_type := 'document.created';
        audit_action := 'create';
        audit_actor := COALESCE(NULLIF(current_setting('app.audit_actor_id', true), ''), NEW.owner_id);
        audit_role := COALESCE(NULLIF(current_setting('app.audit_actor_role', true), ''), 'User');
        audit_details := jsonb_build_object(
            'contentType', NEW.content_type,
            'sizeInBytes', NEW.size_in_bytes,
            'status', NEW.status
        );
    ELSIF OLD.status IS DISTINCT FROM NEW.status THEN
        audit_event_type := 'document.status_changed';
        audit_action := 'update_status';
        audit_actor := COALESCE(NULLIF(current_setting('app.audit_actor_id', true), ''), 'system:ingestion-worker');
        audit_role := COALESCE(NULLIF(current_setting('app.audit_actor_role', true), ''), 'System');
        audit_details := jsonb_build_object(
            'oldStatus', OLD.status,
            'newStatus', NEW.status
        );
    ELSE
        RETURN NEW;
    END IF;

    INSERT INTO audit_events
        (tenant_id, actor_user_id, actor_role, event_type, action, resource_type,
         resource_id, outcome, correlation_id, trace_id, details)
    VALUES
        (NEW.tenant_id,
         audit_actor,
         audit_role,
         audit_event_type,
         audit_action,
         'document',
         NEW.id::text,
         'success',
         COALESCE(NULLIF(current_setting('app.correlation_id', true), ''), 'database-trigger'),
         NULLIF(current_setting('app.trace_id', true), ''),
         audit_details);

    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS trg_audit_document_change ON documents;
CREATE TRIGGER trg_audit_document_change
    AFTER INSERT OR UPDATE OF status ON documents
    FOR EACH ROW
    EXECUTE FUNCTION audit_document_change();

CREATE OR REPLACE FUNCTION audit_ingestion_job_change()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS
$$
DECLARE
    audit_event_type TEXT;
    audit_action TEXT;
    audit_actor TEXT;
    audit_role TEXT;
    audit_details JSONB;
BEGIN
    IF TG_OP = 'INSERT' THEN
        audit_event_type := 'ingestion.queued';
        audit_action := 'queue';
        audit_actor := COALESCE(NULLIF(current_setting('app.audit_actor_id', true), ''), 'system:document-api');
        audit_role := COALESCE(NULLIF(current_setting('app.audit_actor_role', true), ''), 'System');
        audit_details := jsonb_build_object(
            'status', NEW.status,
            'attemptCount', NEW.attempt_count,
            'maxAttempts', NEW.max_attempts
        );
    ELSIF OLD.status IS DISTINCT FROM NEW.status THEN
        audit_event_type := 'ingestion.status_changed';
        audit_action := 'update_status';
        audit_actor := COALESCE(NULLIF(current_setting('app.audit_actor_id', true), ''), 'system:ingestion-worker');
        audit_role := COALESCE(NULLIF(current_setting('app.audit_actor_role', true), ''), 'System');
        audit_details := jsonb_build_object(
            'oldStatus', OLD.status,
            'newStatus', NEW.status,
            'attemptCount', NEW.attempt_count,
            'maxAttempts', NEW.max_attempts,
            'errorCode', NEW.last_error_code
        );
    ELSE
        RETURN NEW;
    END IF;

    INSERT INTO audit_events
        (tenant_id, actor_user_id, actor_role, event_type, action, resource_type,
         resource_id, outcome, correlation_id, trace_id, details)
    VALUES
        (NEW.tenant_id,
         audit_actor,
         audit_role,
         audit_event_type,
         audit_action,
         'ingestion_job',
         NEW.id::text,
         'success',
         COALESCE(NULLIF(current_setting('app.correlation_id', true), ''), 'database-trigger'),
         NULLIF(current_setting('app.trace_id', true), ''),
         audit_details);

    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS trg_audit_ingestion_job_change ON document_ingestion_jobs;
CREATE TRIGGER trg_audit_ingestion_job_change
    AFTER INSERT OR UPDATE OF status ON document_ingestion_jobs
    FOR EACH ROW
    EXECUTE FUNCTION audit_ingestion_job_change();
