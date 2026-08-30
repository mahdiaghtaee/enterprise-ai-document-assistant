from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ALERTS = ROOT / "infra" / "observability" / "alerts.yml"
RUNBOOK = ROOT / "docs" / "runbooks" / "AUDIT_OPERATIONS.md"
SLO = ROOT / "docs" / "SLO_AND_ALERTING.md"
DASHBOARD = ROOT / "infra" / "observability" / "grafana" / "dashboards" / "operations.json"
COMPOSE = ROOT / "docker-compose.observability.yml"
COLLECTOR = ROOT / "infra" / "observability" / "otel-collector.yaml"

EXPECTED_ALERTS = {
    "AuditPersistenceFailure",
    "AuditIntegrityFailure",
    "AuditArchiveFailure",
    "IngestionTerminalFailureSpike",
    "ApiErrorBudgetBurn",
    "ApiLatencySloViolation",
    "TelemetryPipelineUnavailable",
}

EXPECTED_RECORDING_RULES = {
    "document_assistant:api_request_rate5m",
    "document_assistant:api_5xx_rate5m",
    "document_assistant:api_5xx_ratio5m",
    "document_assistant:api_latency_p95_seconds5m",
}

EXPECTED_IMAGES = {
    "otel/opentelemetry-collector-contrib:0.157.0",
    "prom/prometheus:v3.13.1",
    "prom/alertmanager:v0.33.1",
    "grafana/grafana:13.1.0",
}

EXPECTED_HTTP_SERVER_METRIC = "http_server_request_duration_seconds"

FORBIDDEN_METRIC_LABEL_TERMS = {
    "tenant_id",
    "user_id",
    "document_id",
    "file_name",
    "correlation_id",
    "trace_id",
    "question",
    "query_text",
    "answer_text",
    "invitation_token",
    "api_key",
}


def markdown_anchors(markdown: str) -> set[str]:
    anchors: set[str] = set()
    for line in markdown.splitlines():
        if not line.startswith("#"):
            continue
        heading = line.lstrip("#").strip().lower()
        anchor = re.sub(r"[^a-z0-9\s-]", "", heading)
        anchor = re.sub(r"\s+", "-", anchor)
        anchor = re.sub(r"-+", "-", anchor).strip("-")
        if anchor:
            anchors.add(anchor)
    return anchors


def main() -> None:
    alerts_text = ALERTS.read_text(encoding="utf-8")
    runbook_text = RUNBOOK.read_text(encoding="utf-8")
    slo_text = SLO.read_text(encoding="utf-8")
    compose_text = COMPOSE.read_text(encoding="utf-8")
    collector_text = COLLECTOR.read_text(encoding="utf-8")
    dashboard = json.loads(DASHBOARD.read_text(encoding="utf-8"))

    actual_alerts = set(re.findall(r"^\s*- alert:\s*([A-Za-z0-9_:.-]+)\s*$", alerts_text, re.MULTILINE))
    missing_alerts = EXPECTED_ALERTS - actual_alerts
    if missing_alerts:
        raise SystemExit(f"Missing expected alerts: {sorted(missing_alerts)}")

    actual_records = set(re.findall(r"^\s*- record:\s*([^\s]+)\s*$", alerts_text, re.MULTILINE))
    missing_records = EXPECTED_RECORDING_RULES - actual_records
    if missing_records:
        raise SystemExit(f"Missing expected recording rules: {sorted(missing_records)}")

    if "translation_strategy: UnderscoreEscapingWithSuffixes" not in collector_text:
        raise SystemExit("Prometheus exporter translation strategy must explicitly preserve unit/type suffixes")
    if "http_server_request_duration_milliseconds" in alerts_text:
        raise SystemExit("Legacy millisecond HTTP server metric must not be used in Prometheus rules")
    if f"{EXPECTED_HTTP_SERVER_METRIC}_count" not in alerts_text:
        raise SystemExit("API request-rate rules must use the stable seconds-based HTTP server metric")
    if f"{EXPECTED_HTTP_SERVER_METRIC}_bucket" not in alerts_text:
        raise SystemExit("API latency rules must use the stable seconds-based HTTP server histogram")

    anchors = markdown_anchors(runbook_text)
    referenced_anchors = set(
        re.findall(r"runbook:\s*docs/runbooks/AUDIT_OPERATIONS\.md#([a-z0-9-]+)", alerts_text)
    )
    missing_anchors = referenced_anchors - anchors
    if missing_anchors:
        raise SystemExit(f"Alert rules reference missing runbook anchors: {sorted(missing_anchors)}")

    for alert_name in EXPECTED_ALERTS:
        if f"`{alert_name}`" not in slo_text and f"`{alert_name}`" not in runbook_text:
            raise SystemExit(f"Alert {alert_name} is not documented")

    if dashboard.get("uid") != "enterprise-document-assistant-operations":
        raise SystemExit("Grafana dashboard UID is not stable")
    if len(dashboard.get("panels", [])) < 7:
        raise SystemExit("Grafana dashboard does not contain the expected operational panels")

    dashboard_text = json.dumps(dashboard, sort_keys=True)
    if "document_assistant:api_5xx_ratio5m" not in dashboard_text:
        raise SystemExit("Dashboard does not use the API error recording rule")
    if "document_assistant_audit_integrity_failures_total" not in dashboard_text:
        raise SystemExit("Dashboard does not expose audit integrity failures")

    for image in EXPECTED_IMAGES:
        if image not in compose_text:
            raise SystemExit(f"Observability image is not pinned as expected: {image}")
    if ":latest" in compose_text:
        raise SystemExit("Observability Compose file must not use latest image tags")

    metrics_and_dashboard = f"{alerts_text}\n{dashboard_text}".lower()
    for forbidden in FORBIDDEN_METRIC_LABEL_TERMS:
        label_pattern = re.compile(rf"[{{,]\s*{re.escape(forbidden)}\s*=")
        if label_pattern.search(metrics_and_dashboard):
            raise SystemExit(f"Forbidden high-cardinality/sensitive metric label detected: {forbidden}")

    print(
        "Validated observability assets: "
        f"{len(EXPECTED_ALERTS)} alerts, "
        f"{len(EXPECTED_RECORDING_RULES)} recording rules, "
        f"{len(dashboard['panels'])} dashboard panels, pinned images, metric units, and runbook links."
    )


if __name__ == "__main__":
    main()
