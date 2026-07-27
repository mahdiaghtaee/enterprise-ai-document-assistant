# Background Ingestion

## Scope

Document upload now persists document metadata and an initial ingestion job in one PostgreSQL transaction. The HTTP request returns after the file is stored and the durable `Pending` job is created. An ASP.NET Core hosted worker performs extraction, chunking, deterministic embedding generation, and semantic-index persistence outside the upload request.

The implementation includes:

- atomic document and initial job creation;
- ordered transactional claiming with `FOR UPDATE SKIP LOCKED`;
- bounded retry attempts with delayed availability;
- recovery of abandoned `Processing` jobs;
- controlled terminal failure states;
- a public document processing-status endpoint;
- document-level status updates for list responses.

## Upload Contract

```http
POST /api/documents/upload
```

A valid upload returns `202 Accepted`. The response contains:

- the document ID;
- the durable ingestion job ID;
- `queued_for_background_processing` as the indexing status;
- a `processingStatusUrl` for polling.

If database persistence fails after the local file is stored, the uploaded file is removed so it does not become an untracked storage orphan.

## Processing Status

```http
GET /api/documents/{documentId}/processing-status
```

The response reports:

- job and document IDs;
- current state;
- current and maximum attempts;
- availability and lifecycle timestamps;
- controlled error code and summary;
- whether the state is terminal.

A document created through the metadata-only endpoint does not have an ingestion job and returns `404` from this status endpoint.

## Processing States

| State | Meaning |
|---|---|
| `Pending` | The durable job can be claimed at or after `available_at`. |
| `Processing` | A worker claimed the job and incremented `attempt_count`. |
| `Completed` | Extraction and semantic-index persistence completed successfully. |
| `Failed` | A non-retryable error occurred or the retry limit was exhausted. |

The corresponding document status progresses through `uploaded`, `processing`, `retry-pending`, `indexed`, or `failed`.

## Worker Claiming

The worker selects the next available job in this order:

```text
available_at, created_at, id
```

The claim executes inside one PostgreSQL transaction:

1. select one eligible `Pending` row;
2. lock it with `FOR UPDATE SKIP LOCKED`;
3. change it to `Processing`;
4. increment its attempt count;
5. set `started_at`;
6. return the claimed row.

`SKIP LOCKED` allows multiple API instances to claim different jobs without blocking one another or processing the same active job concurrently.

## Retry Policy

Unexpected infrastructure or processing exceptions are retryable. The worker returns the job to `Pending`, records a controlled error, and moves `available_at` forward by the configured retry delay.

Known validation or document-content failures are terminal. Current terminal examples include:

- unsupported content type for extraction;
- missing document metadata;
- empty extracted text;
- no indexable chunks.

A retryable failure becomes terminal when `attempt_count` reaches `max_attempts`. The default maximum is `3`.

## Recovery

The worker periodically looks for `Processing` jobs whose `started_at` is older than the processing timeout. These jobs are considered abandoned, for example after a process crash or host termination.

- jobs with attempts remaining return to `Pending`;
- jobs with no attempts remaining become `Failed`;
- recovered jobs receive the `worker-timeout` error code.

A graceful application shutdown returns the currently interrupted job to `Pending` and restores the consumed attempt so shutdown does not use the retry budget.

## Configuration

The hosted worker uses the `IngestionWorker` configuration section:

```json
{
  "IngestionWorker": {
    "PollInterval": "00:00:02",
    "RetryDelay": "00:00:15",
    "ProcessingTimeout": "00:10:00",
    "RecoveryInterval": "00:01:00"
  }
}
```

All values have safe defaults. They can also be supplied through environment variables, for example:

```text
IngestionWorker__PollInterval=00:00:02
IngestionWorker__RetryDelay=00:00:15
IngestionWorker__ProcessingTimeout=00:10:00
IngestionWorker__RecoveryInterval=00:01:00
```

## Database Schema

Fresh PostgreSQL volumes execute:

```text
infra/postgres/init/zzz-ingestion-jobs.sql
```

The schema enforces:

- the four documented states;
- bounded attempt counters;
- lifecycle timestamp consistency;
- non-empty controlled error fields;
- one active `Pending` or `Processing` job per document;
- indexes for ordered claiming and document history.

## Existing Local Volumes

PostgreSQL entrypoint scripts run only when a database volume is first created. For disposable local data:

```bash
docker compose down --volumes
docker compose up --build
```

Do not remove a volume containing required data. Apply the idempotent initialization script through `psql` after taking a backup.

## Verification

The PostgreSQL integration suite verifies:

- atomic document and job creation;
- rollback when job creation fails;
- active-job uniqueness;
- cancellation rollback;
- claiming and attempt increments;
- successful completion;
- retry requeue behavior;
- terminal failure after retry exhaustion;
- abandoned-job recovery;
- latest status retrieval.
