# End-to-End Demo Flow

This guide shows the main local demo flow for the Enterprise AI Document Assistant.

Start the services first:

```bash
docker compose up --build
```

Base URL:

```text
http://localhost:5000
```

## 1. Health check

```bash
curl http://localhost:5000/health
```

## 2. Upload the sample document

```bash
curl -X POST http://localhost:5000/api/documents/upload \
  -F "file=@samples/sample-policy.txt;type=text/plain"
```

## 3. Search indexed document chunks

```bash
curl -X POST http://localhost:5000/api/documents/search \
  -H "Content-Type: application/json" \
  -d '{
    "query": "vendor contract approval process",
    "topK": 3
  }'
```

## 4. Ask a grounded question

```bash
curl -X POST http://localhost:5000/api/documents/ask \
  -H "Content-Type: application/json" \
  -d '{
    "question": "Who needs to approve vendor contracts?",
    "topK": 3
  }'
```

## Expected demo story

The demo should show this path:

```text
Upload document -> Extract text -> Chunk -> Embed -> Search -> Ask -> Return answer with sources
```

The current ask endpoint is intentionally deterministic and local. It uses indexed source chunks to produce a simple grounded response without calling an external LLM provider.
