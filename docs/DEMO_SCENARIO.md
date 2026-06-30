# Demo Scenario

This document defines a simple business demo for presenting the Enterprise AI Document Assistant to freelance clients.

## Client Story

A mid-sized company has many internal PDF documents: policies, contracts, onboarding guides, operational reports, and compliance notes.

Employees waste time searching through these files manually. The company wants a private AI assistant that can answer questions using only approved internal documents.

## Demo Goal

Show how the backend can support this workflow:

```text
Upload document -> process document -> index knowledge -> ask question -> return answer with sources
```

## Demo Persona

**Operations Manager**

The operations manager wants to quickly answer questions such as:

- What is the approval process for vendor contracts?
- Which department is responsible for compliance review?
- What documents mention onboarding requirements?
- Which policy explains access control?

## Demo Input

Example document:

```text
Internal Vendor Contract Policy

All vendor contracts must be reviewed by the Operations department. Contracts above the approved threshold must also be reviewed by Finance. Final approval must be recorded before the contract is signed.
```

## Demo Questions

```text
Who needs to review vendor contracts?
```

```text
When is Finance approval required?
```

```text
What must happen before the contract is signed?
```

## Expected AI Assistant Behavior

The assistant should:

- Use the uploaded document as the source of truth
- Avoid unsupported claims
- Return a concise business answer
- Include the source document or chunk reference
- Make it clear when the answer is not available in the indexed documents

## Expected Answer Example

Question:

```text
Who needs to review vendor contracts?
```

Answer:

```text
Vendor contracts must be reviewed by the Operations department. If the contract is above the approved threshold, Finance must also review it.
```

Sources:

```text
Internal Vendor Contract Policy
```

## Client Value

This demo shows that the system can support:

- Internal knowledge search
- Policy question answering
- Contract and document review workflows
- Source-grounded AI answers
- Private enterprise AI use cases

## Freelance Conversation Angle

When presenting this project to a potential client, position it as:

> A backend foundation for a private AI assistant that helps teams search and understand internal documents with traceable answers.

## Next Demo Improvements

- Add a sample PDF under `samples/`
- Add a Postman collection
- Add screenshots from Swagger
- Add a short screen recording of the upload and ask flow
- Add a minimal frontend or admin dashboard later
