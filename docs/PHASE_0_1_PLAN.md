# Phase 0 and Phase 1 Execution Plan

This plan keeps the project practical, human, and defensible. The goal is not to add every AI feature at once. The goal is to build a clear product story that a reviewer, recruiter, or client can understand quickly.

## Phase 0: Portfolio Ready

Goal: make the repository credible and easy to understand from the GitHub page.

### Deliverables

- Clear README
- Architecture diagram
- Runnable local demo
- MIT license
- Contributing guide
- Code of conduct
- Issue templates
- Pull request template
- Release notes for the first version
- Screenshot and short GIF after the UI exists

### Definition of Done

A visitor should understand the following within 30 seconds:

- What the project does
- Why it exists
- How to run it
- What is implemented now
- What is planned next

## Phase 1: Usable MVP

Goal: make the project usable, not just documented.

### Deliverables

- Simple Web UI
- Document upload screen
- Document list screen
- Search screen
- Ask/chat screen
- Source chunk viewer
- Sample documents
- Basic integration tests
- PostgreSQL-backed document metadata

### Product Story

The project should demonstrate this flow:

```text
Upload document -> index document -> ask a question -> receive an answer with sources
```

### What Not to Add Yet

These features are useful later, but they should not be added before the MVP is clear:

- Multi-agent workflow
- Large plugin systems
- Complex orchestration frameworks
- Unnecessary abstractions
- Multiple AI providers before one clear provider works well

## Priority Order

1. Make the repository trustworthy.
2. Make the demo easy to run.
3. Add a simple UI.
4. Add persistence.
5. Add real AI provider support.
6. Add enterprise features.
