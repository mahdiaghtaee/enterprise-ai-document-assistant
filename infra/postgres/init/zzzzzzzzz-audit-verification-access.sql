\set ON_ERROR_STOP on

BEGIN;

CREATE OR REPLACE FUNCTION verify_audit_chain_scoped(p_tenant_id TEXT)
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
    v_tenant_context TEXT;
    v_can_read_cross_tenant BOOLEAN;
BEGIN
    IF p_tenant_id IS NULL OR length(btrim(p_tenant_id)) = 0 THEN
        RAISE EXCEPTION 'tenant id is required' USING ERRCODE = '22023';
    END IF;

    v_can_read_cross_tenant :=
        pg_has_role(session_user, 'document_platform', 'member')
        OR pg_has_role(session_user, 'document_privileged', 'member')
        OR session_user IN ('document_platform', 'document_privileged');

    IF NOT v_can_read_cross_tenant THEN
        v_tenant_context := NULLIF(current_setting('app.tenant_id', true), '');
        IF v_tenant_context IS NULL OR v_tenant_context <> p_tenant_id THEN
            RAISE EXCEPTION 'audit chain verification is outside the tenant context'
                USING ERRCODE = '42501';
        END IF;
    END IF;

    RETURN QUERY
        SELECT * FROM verify_audit_chain(p_tenant_id);
END
$$;

REVOKE ALL ON FUNCTION verify_audit_chain(TEXT)
    FROM PUBLIC, document_app, document_platform, document_privileged;
REVOKE ALL ON FUNCTION verify_audit_chain_scoped(TEXT) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION verify_audit_chain_scoped(TEXT)
    TO document_app, document_platform, document_privileged;

COMMIT;
