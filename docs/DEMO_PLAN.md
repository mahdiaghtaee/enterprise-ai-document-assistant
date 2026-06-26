# Demo Plan

This document defines the first useful demo for Enterprise AI Document Assistant.

## Demo Goal

Show a simple end-to-end workflow that a client can understand in less than two minutes.

## Target Demo Flow

1. Start the local stack with Docker Compose
2. Open Swagger UI
3. Upload a sample document
4. List uploaded documents
5. Send the document to the AI service for indexing
6. Ask a question about the document
7. Return an answer with a document reference

## First Demo Scope

The first demo does not need advanced AI quality. It should prove that the system architecture works:

- API is running
- AI service is running
- Document upload works
- Service-to-service communication works
- The project can be explained clearly

## Demo Assets Needed

- One sample PDF or text document
- Screenshot of Swagger UI
- Screenshot of document upload response
- Screenshot of document list response
- Screenshot of the planned question-answer endpoint
- Short screen recording after the first working flow is stable

## Success Criteria

The demo is ready when a client can understand:

- What problem the project solves
- How the system is structured
- How it can become a private document assistant
- What the next implementation steps are

## Next Step

Implement a minimal question-answer endpoint after document metadata persistence and basic text extraction are available.
