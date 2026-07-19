# Background Ingestion Foundation

## Scope

This milestone introduces the durable PostgreSQL job model and the public processing-state contract required for asynchronous document ingestion.

It does not yet activate a background worker. Upload, extraction, chunking, embedding generation, and semantic-index writes remain synchronous until the worker and atomic enqueue path are implemented in follow-up pull requests.

## Processing States

| State | Meaning |
|---|---|
| `Pending` | The job is durable and available for a worker to claim at or after `available_at`. |
| `Processing` | A worker has claimed the job and set `started_at`. |
| `Completed` | Extraction and indexing finished successfully and `completed_at` is set. |
| `Failed` | The job reached a controlled failure state and `failed_at` is set. |

The state names are represented by `DocumentIngestionStatus` in the ASP.NET Core project and constrained by PostgreSQL.

## Database Schema

Fresh PostgreSQL volumes execute:

```text
infra/postgres/init/zzz-ingestion-jobs.sql
```

The `document_ingestion_jobs` table includes:

- an identity job ID;
- a cascading reference to the owning document;
- state and lifecycle timestamps;
- bounded attempt counters;
- delayed availability for future retries;
- controlled error code and summary fields;
- created and updated timestamps.

## Invariants

The schema enforces:

- only the four documented states are accepted;
- attempt counts cannot be negative or exceed `max_attempts`;
- lifecycle timestamps must match the selected state;
- empty error strings are rejected;
- a document can have historical jobs but only one `Pending` or `Processing` job at a time.

## Worker Claiming Direction

A partial index on pending jobs orders candidates by:

```text
available_at, created_at, id
```

The worker implementation should claim rows transactionally with PostgreSQL row locking, such as `FOR UPDATE SKIP LOCKED`, and change the selected row to `Processing` in the same transaction.

The exact claiming query, retry policy, recovery behavior, and status endpoint are follow-up work under issue #5.

## Existing Local Volumes

PostgreSQL entrypoint scripts run only when a database volume is first created. For disposable local data:

```bash
docker compose down --volumes
docker compose up --build
```

Do not remove a volume containing required data. Apply the idempotent initialization script through `psql` after taking a backup.

## Verification

CI starts a fresh Docker Compose stack and checks:

- the ingestion-job table exists;
- the status constraint exists;
- the active-job uniqueness index exists;
- the pending-job claim index exists;
- the default status is `Pending`;
- the default retry limit is `3`.
