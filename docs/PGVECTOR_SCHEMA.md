# pgvector Schema Foundation

## Scope

This document covers the database foundation for persistent semantic-index records. It does not activate PostgreSQL as the application semantic-index provider. The ASP.NET Core API continues to use the in-memory `ISemanticIndexStore` until the provider implementation and integration tests are added in a follow-up change.

## Local PostgreSQL Image

Docker Compose uses the pinned image:

```text
pgvector/pgvector:0.8.5-pg16
```

Pinning the pgvector and PostgreSQL major versions keeps local and CI initialization reproducible.

## Initialization Script

Fresh PostgreSQL volumes execute:

```text
infra/postgres/init/zz-pgvector-schema.sql
```

The script is intentionally ordered after the base document schema. It:

- enables the `vector` extension;
- creates `document_chunks`;
- stores the current deterministic embeddings as `vector(8)`;
- enforces one row per document and chunk index;
- deletes chunks when their document is deleted;
- adds an HNSW cosine-distance index.

## Schema

| Column | Type | Purpose |
|---|---|---|
| `document_id` | `uuid` | References the owning document |
| `chunk_index` | `integer` | Stable position within the document |
| `content` | `text` | Extracted chunk text |
| `embedding` | `vector(8)` | Current deterministic embedding |
| `created_at` | `timestamptz` | Initial persistence time |
| `updated_at` | `timestamptz` | Last replacement time |

The primary key is `(document_id, chunk_index)` so provider upserts can remain idempotent.

The fixed dimension must match `DeterministicEmbeddingGenerator`, which currently emits eight values. A future embedding provider with a different dimension requires an explicit schema migration instead of silently mixing vector sizes.

## Existing Local Volumes

PostgreSQL entrypoint scripts run only when a database volume is first created. Switching the container image or changing the initialization script does not rerun initialization against an existing volume.

For disposable local data, recreate the volume:

```bash
docker compose down --volumes
docker compose up --build
```

Do not remove a volume that contains data you need. Back it up before applying a schema migration.

## Verification

After starting a fresh stack:

```bash
docker compose exec -T postgres psql -U documents -d documents -c "SELECT extversion FROM pg_extension WHERE extname = 'vector';"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ document_chunks"
```

CI performs equivalent checks for the extension, eight-dimensional vector column, and cosine index.

## Follow-up

The next pull request should implement a PostgreSQL-backed `ISemanticIndexStore`, select the provider through configuration, and preserve the existing public search and ask contracts.
