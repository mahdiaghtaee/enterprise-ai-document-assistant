# FastAPI 0.142 Compatibility Review

## Scope

This review upgrades the Python boundary service from FastAPI 0.115.6 to 0.142.1 while keeping Python 3.11, Pydantic 2.10.4, Uvicorn 0.54.0, and the coordinated OpenTelemetry API/SDK/exporter 1.45.0 plus FastAPI instrumentation 0.66b0 family.

The Python service remains a narrow health/index boundary. This change does not move document ingestion, authorization, tenant policy, semantic indexing, or grounded-answer orchestration from the ASP.NET Core application into FastAPI.

## Compatibility checks

The migration is accepted only when the repository's normal validation matrix passes. The Python-specific contract checks cover:

- `TestClient` request/response behavior;
- correlation-header middleware on success and validation failures;
- Pydantic request validation for required `file_name`;
- OpenAPI schema generation for `/health` and `/index`;
- OpenTelemetry FastAPI instrumentation startup;
- Docker image dependency resolution;
- Compose health startup.

Repository-level CI additionally validates PostgreSQL integration, safe document processing, audit/observability, operational observability, retrieval, grounded answers, multilingual quality, Dependency Review, and CodeQL.

## Behavioral boundary

No API contract change is intended. Existing endpoints, response fields, correlation behavior, and validation status codes remain unchanged. The upgrade does not by itself establish production readiness or justify moving additional processing into Python.

## Rollback

If a deployment-specific incompatibility is found, revert the FastAPI pin to `0.115.6` while keeping the independently validated Uvicorn and OpenTelemetry maintenance versions. No database migration or persistent-data rollback is required for this dependency-only change.
