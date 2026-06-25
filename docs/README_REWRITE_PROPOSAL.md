# README Rewrite Proposal

This file contains the client-focused README direction for the repository.

## Project Summary

Enterprise AI Document Assistant is a practical document assistant for teams that need to organize internal files and find answers faster.

The backend is developed with ASP.NET Core, and document intelligence features are handled by a separate Python service.

## Purpose

The project demonstrates how to design a client-ready system for document upload, metadata management, service-to-service communication, and future retrieval-based question answering.

## Current Features

- ASP.NET Core API
- Python FastAPI service
- Docker Compose local setup
- PostgreSQL container for future persistence
- Redis container for future background jobs and caching
- Local document upload workflow
- In-memory document metadata repository
- Health endpoints for both services
- Swagger UI for API testing

## Planned Features

- PostgreSQL document metadata storage
- Background worker for document indexing
- Document parsing and chunking
- Semantic search
- Question-answer workflow with document references
- Authentication and role-based access
- Integration tests and CI

## Architecture Direction

The system is intentionally split into two services.

The ASP.NET Core API owns the application workflow: upload, metadata, validation, and client-facing endpoints.

The Python service owns document processing and AI-related work.

## Portfolio Positioning

This project is designed to represent real client work around backend development, document automation, and AI integration.

It can support conversations about:

- Internal document search
- Company knowledge assistants
- Research document tools
- Private business document assistants

## Next README Improvements

- Add screenshots after the first stable demo
- Add sample API responses
- Add a short demo video link
- Add deployment notes after the local demo is stable
