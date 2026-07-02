# Text Extraction Pipeline

## Purpose

The text extraction pipeline is the first implemented document-processing step for the RAG workflow.

It turns uploaded documents into normalized text that can later be chunked, embedded, indexed, and used for retrieval-augmented generation.

---

## Current Scope

The current implementation supports plain text documents:

```text
text/plain
```

Other uploadable document types, such as PDF and DOCX, are still accepted by the upload validator, but text extraction currently returns a clear `unsupported-content-type` result for them until dedicated extractors are added.

---

## Upload Flow

```text
Upload document
  -> Validate upload
  -> Save file to local storage
  -> Store document metadata
  -> Extract normalized text
  -> Queue indexing only when extraction succeeds
  -> Return upload response with extraction summary
```

---

## Extraction Result

The extraction component returns a structured result containing:

- `succeeded`
- `text`
- `characterCount`
- `errorCode`
- `message`

The upload response exposes a safe extraction summary containing:

- success/failure state
- extracted character count
- short text preview
- error code when extraction fails
- human-readable failure message

---

## Failure Cases

The extractor currently handles these cases explicitly:

| Error Code | Meaning |
|---|---|
| `unsupported-content-type` | The file type does not yet have a text extractor. |
| `document-not-found` | The stored file path does not exist. |
| `empty-extracted-text` | The file was readable but did not contain useful text. |

---

## Next Steps

Planned improvements:

1. Add PDF extraction support.
2. Add DOCX extraction support.
3. Store extracted text or extracted text references persistently.
4. Add chunking after successful extraction.
5. Queue background indexing instead of direct indexing from the upload request.
