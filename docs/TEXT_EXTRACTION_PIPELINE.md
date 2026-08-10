# Safe Document Extraction Pipeline

## Purpose

The ingestion boundary accepts only document formats the worker can process safely and predictably. Validation happens before durable enqueue, while text extraction happens in the independent privileged worker.

Supported formats:

| Extension | Declared content type | Worker extraction |
|---|---|---|
| `.txt` | `text/plain` | strict UTF-8 text |
| `.pdf` | `application/pdf` | PdfPig content-order extraction |
| `.docx` | `application/vnd.openxmlformats-officedocument.wordprocessingml.document` | bounded WordprocessingML extraction |

The implementation does not trust the filename extension or multipart MIME value by itself.

## Upload Security Gates

```text
Authenticated tenant member
  -> size/content-type/extension validation
  -> extension and MIME agreement
  -> file signature/package inspection
  -> optional malware scan
  -> local file persistence
  -> atomic document + Pending ingestion job
  -> independent worker extraction/chunk/embed/index
```

A rejected inspection or malware scan occurs before file persistence and before document/job creation.

### TXT

- maximum upload size remains 10 MB;
- `.txt` and `text/plain` must agree;
- initial bytes may not contain binary NUL values;
- initial bytes must be valid UTF-8;
- the worker reads the full document with strict UTF-8 decoding and fails rather than replacing invalid bytes.

### PDF

- `.pdf` and `application/pdf` must agree;
- bytes must begin with the PDF `%PDF-` signature;
- PdfPig must be able to parse the document before enqueue;
- page count is bounded before enqueue and checked again by the worker;
- worker text uses PdfPig's content-order extractor rather than relying directly on `Page.Text`;
- a PDF with no extractable text returns `ocr-required` rather than creating empty semantic chunks.

PDF text order is inherently layout-dependent. The current extractor is suitable for controlled text PDFs but is not a general document-layout reconstruction engine.

### DOCX

DOCX is treated as an untrusted ZIP/OOXML package.

Before enqueue the API checks:

- valid ZIP structure;
- bounded archive-entry count;
- bounded total uncompressed size;
- no absolute or `..` traversal paths;
- `[Content_Types].xml` exists;
- `word/document.xml` exists;
- the main Word document content type is declared correctly;
- XML parses with DTD processing prohibited and no external resolver.

The worker repeats archive limits before extraction. WordprocessingML text nodes, paragraph boundaries, tabs, and line breaks are converted into normalized plain text.

## Configured Safety Limits

Defaults:

| Setting | Default |
|---|---:|
| `DocumentProcessing:MaxPdfPages` | `200` |
| `DocumentProcessing:MaxDocxArchiveEntries` | `2048` |
| `DocumentProcessing:MaxDocxExpandedBytes` | `52428800` (50 MB) |
| `DocumentProcessing:MaxExtractedCharacters` | `1000000` |
| `DocumentProcessing:MaxDocxXmlCharacters` | `5000000` |
| upload size | `10 MB` |

The worker's existing processing timeout and cancellation token provide the outer execution-time boundary. The extractor checks cancellation while reading text, pages, archive entries, and XML.

## Malware-Scanning Boundary

`FileThreatScanning:Provider` supports:

- `Disabled` — local default; no external malware service is called;
- `ClamAv` — uses the clamd `INSTREAM` protocol over TCP.

When ClamAV is enabled:

- `OK` allows the upload;
- `FOUND` rejects the upload with `malware-detected`;
- timeout, socket failure, I/O failure, or an unexpected scanner response fails closed with `malware-scanner-unavailable`;
- raw scanner responses and signature names are not returned, audited, or used as metric dimensions.

The reference Compose stack intentionally does not start a ClamAV container. Production deployments must supply and secure their own trusted scanner endpoint before setting the provider to `ClamAv`.

## Controlled Failure Codes

| Error code | Meaning |
|---|---|
| `invalid-file-signature` | bytes/package do not match the declared format |
| `invalid-pdf-file` | PDF signature exists but the document cannot be parsed safely |
| `pdf-page-limit-exceeded` | PDF exceeds configured page limit |
| `invalid-docx-package` | required OOXML structure/XML is invalid |
| `docx-archive-limit-exceeded` | DOCX entry/expanded-size boundary was exceeded |
| `invalid-text-file` | text upload contains binary NUL data |
| `invalid-text-encoding` | text is not valid UTF-8 |
| `extracted-text-limit-exceeded` | extracted text exceeds the configured character limit |
| `ocr-required` | PDF has no extractable text |
| `empty-extracted-text` | supported document contains no readable text |
| `document-not-found` | worker cannot find the persisted upload |
| `unsupported-content-type` | no extractor exists for the stored content type |
| `pdf-extraction-failed` | worker could not safely parse/extract the PDF |
| `docx-extraction-failed` | worker could not safely parse/extract the DOCX |
| `malware-detected` | configured scanner reported a threat |
| `malware-scanner-unavailable` | configured scanner could not produce a trusted clean result |

## Testing

The repository includes:

- deterministic PDF fixtures generated with PdfPig;
- deterministic DOCX ZIP/OOXML fixtures;
- extension/MIME mismatch tests;
- fake-signature and malformed-package tests;
- page, expansion, XML, and extracted-character limit tests;
- image-only PDF behavior tests;
- in-process fake ClamAV TCP tests for `OK`, `FOUND`, and unavailable outcomes;
- a dedicated Compose workflow that uploads real PDF and DOCX documents, waits for the independent worker, verifies retrieval, and confirms spoofed PDF rejection.

## Remaining Format Work

Still intentionally deferred:

- OCR execution for scanned/image-only PDFs;
- richer PDF layout/table reconstruction;
- legacy `.doc` support;
- password-protected document workflows;
- production malware-engine deployment and signature-update operations;
- content-disarm/reconstruction or sandboxed document rendering.
