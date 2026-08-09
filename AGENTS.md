Scope: This file governs the entire repository.
Read this first if you’re contributing, reviewing, or acting as an automated coding agent.

## Reading Order

docs/backlog.md

## Process

- Keep changes small and focused; work in small iterations.
- Every change must be built and tested before being commited
- Commit every change once successful
- if codebase-memory-mcp is available, index the codebase after updates
- Prefer the simplest design that satisfies current requirements.
- If multiple options exist, document a brief rationale and link docs/architecture-decision-records.md
- User instructions take precedence over the central doc.

# Observability

- Use semantic logging
- Use OpenTelemetry

## Coding principles

- Each module serves a problem domain or purpose (SRP)
- New features added via extension, not modification (OCP)
- Subclasses work anywhere base class is used (LSP)
- Interfaces are minimal and focused (ISP)
- Business logic knows nothing about concrete implementations (DIP)
- Public functions have a concise comment explaining their purpose
- Prefer libraries over custom code
- Avoid libraries without open source license
- Use structured exception handling
- No unused fields/methods/usings

## Security

- Security by default: encryption at rest & in transit, least privilege
- Do not store passwords in code

## C#

- Follow common coding conventions
- Use NSubstitute for mocking
- Modern C#: nullable enabled; warnings as errors; primary constructors where helpful
- Async‑first; propagate CancellationToken; Async suffix for async methods
- Use newest available LTS framework

## node.js

- Use typescript
- Prefer ESM over commonjs
- Use pino for logging
- Use the opentelemetry auto instrumentation
- Commits must be eslint clean and tsc --noEmit passes without error

## Task Tooling

- Prefer PowerShell when on Windows

## Code Organization

- src/: source code and tests
- doc/: documentation
- One class, stuct, record or interface per file


