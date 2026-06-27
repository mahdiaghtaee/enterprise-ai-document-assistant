# Architectural Decisions

This document records the major architectural decisions for the project.

## ADR-001: Split .NET API and Python AI Service
Decision: Separate business APIs from AI processing.
Reason: Independent deployment, scaling, and technology evolution.

## ADR-002: Retrieval-Augmented Generation (RAG)
Decision: Answers must be grounded in uploaded documents.
Reason: Reduce hallucinations and provide traceable responses.

## ADR-003: Background Processing
Decision: Long-running document ingestion executes asynchronously.
Reason: Better user experience and scalability.

## ADR-004: Vector Store Abstraction
Decision: Hide vector database implementation behind an interface.
Reason: Allow future provider changes without impacting application logic.

## ADR-005: Multi-Tenant Design
Decision: Design for isolated tenant data from the beginning.
Reason: Enterprise deployments require strong data isolation.
