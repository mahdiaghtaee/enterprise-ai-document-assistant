# Portfolio Summary

## Project

Enterprise AI Document Assistant

## Purpose

A local-first, multi-service document assistant that demonstrates how ASP.NET Core and Python can work together in an enterprise-style backend architecture.

## My Contribution

I designed the architecture, documented the service boundaries, implemented the project foundation, and organized the repository so another engineer can run, review, test, and extend it.

The project currently covers:

- document upload and metadata management;
- PostgreSQL persistence for document metadata;
- Python-based extraction, chunking, embeddings, and retrieval;
- semantic search and source-aware answer construction;
- Docker Compose orchestration;
- a small Web UI and Swagger documentation;
- integration tests, health checks, sample documents, and demo scripts.

## Engineering Decisions

- Kept business-facing workflows in ASP.NET Core.
- Isolated document intelligence in a Python service.
- Used deterministic local embeddings to keep tests reproducible and avoid provider setup.
- Kept current limitations explicit instead of presenting the project as production-ready.
- Documented the path toward pgvector, background processing, authentication, observability, and audit logging.

## Resume-ready Description

Designed and implemented a multi-service enterprise document-assistant reference architecture using ASP.NET Core, Python FastAPI, PostgreSQL, Redis, and Docker Compose. Built document upload, metadata persistence, text chunking, deterministic embeddings, semantic retrieval, source-aware question answering, integration tests, health endpoints, and reproducible local demos. Documented production trade-offs and an evolution path toward pgvector, background indexing, authentication, audit logging, and observability.

## Interview Topics

- .NET and Python service integration
- API and service-boundary design
- RAG pipeline architecture
- deterministic testing for AI-enabled systems
- PostgreSQL and future vector persistence
- production-readiness gaps and migration planning
