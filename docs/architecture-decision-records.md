# Architecture Decision Records

Short records of decisions that are hard to reverse. New features that have more than one
plausible design link here from the code or the backlog.

---

## ADR-001 — Conversation audit log: one JSONL file per conversation plus an append-only index

**Date:** 2026-10-27
**Status:** Accepted

### Context

The proxy needs to record every AI prompt, response, tool call, thinking (reasoning) block
and error so an operator can replay a conversation later. The existing usage log
(`tokens-<date>.jsonl`) is an aggregate-per-request file and is not suited to replay.

### Decision

Store the audit trail as **one JSON Lines file per conversation** under
`<logs>/conversations/<conversation-id>.jsonl`. Each line is a `ConversationEvent`:

```json
{"kind":"response","timestamp":"...","conversationId":"...","requestId":"...",
 "provider":"OpenAI","model":"gpt-4","path":"/v1/chat/completions","data":{...}}
```

Event kinds: `request`, `thinking`, `response`, `tool_call`, `tool_result`, `error`.
A `request` event carries the whole inbound body; the assistant side is decomposed so
thinking and each tool call are individually visible on the timeline.

Listing conversations is served from a separate **append-only index**
(`<logs>/conversations/index.jsonl`), mirrored in memory by `ConversationIndex`.
One line per conversation update; last line per id wins on reload. If the index is
missing it is rebuilt by streaming every conversation file once.

### Alternatives considered

- **Single daily log with a conversation id field** — cheapest to write, but listing and
  replaying one conversation means scanning the whole day, and "one file per conversation"
  was an explicit requirement.
- **Scan conversation files on every list request** — no index to keep in sync, but paging
  and date filtering require opening and summarising every file on every request, which
  degrades linearly with history size.
- **SQLite** — great queries, but adds a binary dependency and a migration surface to a
  service that today has no database; JSONL keeps the log greppable and backup trivial.

### Consequences

- Writes are append-only and crash-safe; a truncated last line is skipped with a warning.
- The index is derived data and can always be deleted and rebuilt.
- Payload fields longer than `Conversations:MaxFieldChars` are truncated so one huge
  request cannot dominate a file.

---

## ADR-002 — Conversation identity: explicit header, else hash of the opening messages

**Date:** 2026-10-27
**Status:** Accepted

### Context

The proxy is stateless and OpenAI-style clients resend the entire message history on every
turn, so nothing in a single request says "this is turn 7 of the same chat".

### Decision

1. Use the `X-Conversation-Id` request header when the client supplies one.
2. Otherwise derive a stable id: `SHA-256(first system message + first user message)`,
   first 16 hex characters. Because history is replayed, every turn of the same chat
   produces the same id and lands in the same file.
3. The request id is a fresh `Guid` per HTTP request and distinguishes turns inside a file.

### Alternatives considered

- **Header only** — without it every request becomes its own conversation, which makes the
  log useless for the many clients that cannot set custom headers.
- **Hash only** — ignores the one signal a client can give us deliberately; also collides
  when two unrelated chats start from identical opening messages.

### Consequences

- Derived ids group by conversation opening, so a client that edits its first message
  starts a new file. Acceptable for an audit log and documented.
- Ids are hashed, so no user content leaks into file names.

---

## ADR-003 — Conversations REST API lives under `/admin/api/conversations`

**Date:** 2026-10-27
**Status:** Accepted

### Decision

`GET /admin/api/conversations` (list, with date filter and paging) and
`GET /admin/api/conversations/{id}` (single conversation) sit alongside the existing
`/admin/api/providers` and `/admin/api/usage/*` endpoints so the whole operator surface
shares one prefix, one auth story and one OpenAPI grouping.

### Alternatives considered

A top-level `/api/conversations` is cleaner in isolation but would split the admin surface
into two conventions for no functional gain today.

---

## ADR-004 — Full content is recorded by default, with a kill switch

**Date:** 2026-10-27
**Status:** Accepted

### Decision

Full prompt, response, thinking and tool payloads are recorded by default; that is the
point of the audit log. `Conversations.Enabled = false` turns recording off completely,
and `Conversations.MaxFieldChars` bounds individual fields. API keys are never written:
the `Authorization` header is not part of the captured payload.

### Consequences

- The conversation directory contains user content and must be secured at the filesystem
  level; it is git-ignored and documented as sensitive.
- Operators in regulated environments must set `Conversations.Enabled=false` or point
  `Conversations:Directory` at appropriately protected storage.
