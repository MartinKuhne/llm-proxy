Scope: This file governs the entire repository.
Read this first if you’re contributing, reviewing, or acting as an automated coding agent.

## Reading Order

docs/backlog.md

## Intent & Principles

- SOLID, KISS, YAGNI
- Security by default: encryption at rest & in transit, least privilege
- Testability: modular boundaries, deterministic components, fast tests first
- Clarity: idiomatic C#/.NET naming, minimal non‑obvious comments only

## Expectations for Agents/Contributors

- Keep changes small and focused; propose ADRs for deviations.
- Add/Update tests for essential behaviors you change or add.
- For each new feature, add both unit and integration tests when feasible. Integration tests are as important as unit tests and should exercise end-to-end behavior without relying on brittle environment assumptions.
- Structured logging only; no Console.WriteLine in production code.

## Session Handoff Protocol (GitHub Issues)

- Start: pick a ready P0 issue, self‑assign, post a “Session Start” plan.
- During: post concise updates at milestones; adjust labels as needed.
- End: post “What landed” + “Next steps” and update labels/boards.
- If behavior/architecture changed, update docs/00-central-design.md in the same commit.

### Task Tooling (GitHub)

- Windows PowerShell (preferred on Windows):

## Code Organization

- src/ - source code and tests

### File Layout Rules (Vertical Slice)

- One type per file: each class/record/struct/enum in its own file named after the type.
- One interface per file: the filename matches the interface name.

## Workflow & Quality

- Follow .NET conventions; keep functions single‑purpose.
- Dependency injection at boundaries;
- Long‑running tooling must run with timeouts/non‑interactive flags.
- Data access (server): API → Application services → Infrastructure (Repository)
- Error handling: return typed results; log structured context; never swallow exceptions.
- Source control: push cohesive changes to master after green build/tests.
- Keep the repo clean: do not commit generated artifacts or logs. .gitignore excludes bin/, obj/, artifacts/, logs/, win-mirror/.

## Coding Standards

- Async‑first; propagate CancellationToken; Async suffix for async methods.
- Prefer await using for IAsyncDisposable resources.
- EF Core: entities/value objects in Domain, mappings in Infrastructure, migrations per feature.
- Modern C#: nullable enabled; warnings as errors; primary constructors where helpful.
- One type per file; one interface per file; interfaces live in Abstractions/ per slice.
- No dead code: remove unused fields/methods/usings and scaffolding when no longer used.
- Naming: interfaces IName, types PascalCase, methods PascalCase, private fields _camelCase, locals/params camelCase.
- Logging: structured with message templates and relevant context; no console logging in prod.

## Documentation Rules

- Central doc is the source of truth. Keep it current when architecture shifts.
- All task/progress tracking in GitHub Issues.

## Ambiguity

- Prefer the simplest design that satisfies current requirements.
- If multiple options exist, document a brief rationale and link docs/00-central-design.md.
- User instructions take precedence over the central doc. 