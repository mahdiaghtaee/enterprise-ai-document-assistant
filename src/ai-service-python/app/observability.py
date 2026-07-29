from __future__ import annotations

import contextvars
import logging
import os
import re
import secrets
from typing import Final

from fastapi import FastAPI
from opentelemetry import metrics, trace
from opentelemetry.exporter.otlp.proto.http.metric_exporter import OTLPMetricExporter
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.metrics import Counter
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

CORRELATION_HEADER: Final = "X-Correlation-ID"
_CORRELATION_PATTERN: Final = re.compile(r"^[A-Za-z0-9._:-]{1,128}$")
_correlation_id: contextvars.ContextVar[str | None] = contextvars.ContextVar(
    "correlation_id", default=None
)


def configure_observability(app: FastAPI) -> Counter:
    service_name = os.getenv(
        "OTEL_SERVICE_NAME", "enterprise-document-assistant-ai-service"
    )
    resource = Resource.create(
        {
            "service.name": service_name,
            "service.namespace": "enterprise-document-assistant",
            "deployment.environment.name": os.getenv("APP_ENVIRONMENT", "Development"),
        }
    )
    endpoint = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT", "").strip().rstrip("/")

    tracer_provider = TracerProvider(resource=resource)
    if endpoint:
        tracer_provider.add_span_processor(
            BatchSpanProcessor(OTLPSpanExporter(endpoint=f"{endpoint}/v1/traces"))
        )
    trace.set_tracer_provider(tracer_provider)

    metric_readers = []
    if endpoint:
        metric_readers.append(
            PeriodicExportingMetricReader(
                OTLPMetricExporter(endpoint=f"{endpoint}/v1/metrics")
            )
        )
    metrics.set_meter_provider(
        MeterProvider(resource=resource, metric_readers=metric_readers)
    )

    FastAPIInstrumentor.instrument_app(app, excluded_urls="health")
    meter = metrics.get_meter("EnterpriseDocumentAssistant.AiService")
    return meter.create_counter(
        "document_assistant.ai.index.requests",
        unit="{request}",
        description="Number of AI-service index boundary requests.",
    )


def resolve_correlation_id(candidate: str | None) -> str:
    if candidate:
        normalized = candidate.strip()
        if _CORRELATION_PATTERN.fullmatch(normalized):
            return normalized
    return secrets.token_hex(16)


def set_correlation_id(value: str) -> contextvars.Token:
    return _correlation_id.set(value)


def reset_correlation_id(token: contextvars.Token) -> None:
    _correlation_id.reset(token)


def get_correlation_id() -> str | None:
    return _correlation_id.get()


def current_trace_id() -> str | None:
    span_context = trace.get_current_span().get_span_context()
    if not span_context.is_valid:
        return None
    return f"{span_context.trace_id:032x}"


def configure_structured_logging() -> None:
    logging.basicConfig(
        level=os.getenv("LOG_LEVEL", "INFO"),
        format=(
            '{"timestamp":"%(asctime)s","level":"%(levelname)s",'
            '"logger":"%(name)s","message":"%(message)s"}'
        ),
    )
