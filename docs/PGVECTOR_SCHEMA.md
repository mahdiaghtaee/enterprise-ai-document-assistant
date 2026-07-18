# pgvector Semantic Index

## Scope

PostgreSQL with pgvector is the persistent semantic-index provider used by the Docker Compose environment. The provider stores document chunks and deterministic eight-dimensional embeddings, performs cosine-distance search in PostgreSQL, and preserves indexed records when the ASP.NET Core API restarts.

The in-memory provider remains available for isolated tests and host processes that do not select PostgreSQL.

## Provider Configuration

Select the provider with:

```text
SemanticIndex__Provider=Postgres
```

Supported values:

| Value | Behavior |
|---|---|
| `InMemory` | Process-local records; default when configuration is absent |
| `Postgres` | Persistent pgvector records and database similarity search |

Docker Compose sets `Postgres` for the API container.

## Local PostgreSQL Image

Docker Compose uses the pinned image:

```text
pgvector/pgvector:0.8.5-pg16
```

Pinning the pgvector and PostgreSQL major versions keeps local and CI behavior reproducible.

## Initialization Script

Fresh PostgreSQL volumes execute:

```text
infra/postgres/init/zz-pgvector-schema.sql
```

The script:

- enables the `vector` extension;
- creates `document_chunks`;
- stores deterministic embeddings as `vector(8)`;
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

The primary key is `(document_id, chunk_index)`. Provider writes use `ON CONFLICT` so repeated writes for the same chunk replace its text and embedding instead of creating duplicates.

The fixed dimension matches `DeterministicEmbeddingGenerator`. A future embedding provider with a different dimension requires an explicit schema migration.

## Retrieval

`PostgresSemanticIndexStore`:

- validates dimensions and rejects non-finite values before opening a connection;
- writes a batch inside one database transaction;
- uses pgvector cosine distance for ranking;
- joins the `documents` table to preserve file-name source metadata;
- keeps public search and ask response contracts independent of pgvector types.

## Existing Local Volumes

PostgreSQL entrypoint scripts run only when the database volume is first created. Changing the initialization script does not update an existing volume.

For disposable local data:

```bash
docker compose down --volumes
docker compose up --build
```

Do not remove a volume containing required data. Back it up and apply a reviewed schema migration instead.

## Verification

```bash
docker compose exec -T postgres psql -U documents -d documents -c "SELECT extversion FROM pg_extension WHERE extname = 'vector';"
docker compose exec -T postgres psql -U documents -d documents -c "\d+ document_chunks"
```

CI additionally uploads a sample document, searches it, restarts the API container, searches again, and verifies that chunk rows remain in PostgreSQL.
