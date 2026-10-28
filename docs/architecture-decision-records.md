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
A `request` event carries the inbound body with `messages` removed and only the messages
the log did not hold yet in `data.messages` — clients resend the whole history every turn,
so storing it verbatim would write it once per turn. See the
[Conversations API contract](conversations-api.md) for the exact fields. The assistant
side is decomposed so thinking and each tool call are individually visible on the
timeline.

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
- Two different clients whose conversation opens identically — the same boilerplate
  system prompt plus the same first user message — derive the same id and land in the
  same conversation file, where their turns interleave. This is not confined to the
  rejected "hash only" alternative above: it applies to every request that reaches the
  derived path, i.e. every client that does not send `X-Conversation-Id`.
- That merge is mitigated rather than eliminated: every proxy response echoes the resolved
  id in `X-Conversation-Id`, so a client that can set headers stops relying on the
  derivation after the first turn. A per-client discriminator in the seed (API-key or
  principal hash) would separate non-cooperating clients too, but an unstable discriminator
  splits one conversation across files, so it is left in the backlog rather than guessed at.
- A client-supplied `X-Conversation-Id` is accepted only when it matches
  `[A-Za-z0-9._-]` up to 100 characters. Anything else is ignored and the request
  derives its own id, so a client cannot name a conversation it does not own and cannot
  fold two distinct ids onto one file.

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

### Consequences

- Placing the read API on the admin surface means it inherits that surface's access control,
  which is what makes the exposure of full conversation content defensible. The proxy has no
  user authentication of its own, so the admin surface is closed to non-loopback peers by
  default (`Admin:RequireLocalhost`, ADR-005). Had the conversations API sat on a public
  prefix instead, adding it would have meant shipping every user prompt, reasoning block and
  tool result on an unauthenticated endpoint.
- The one-auth-story claim is now real rather than aspirational: `/admin/*`, `/api/providers*`
  and the operator pages are gated by the same middleware, so there is a single place to
  change when real authentication arrives.

---

## ADR-004 — Full content is recorded by default, with a kill switch

**Date:** 2026-10-27
**Status:** Accepted

### Decision

Full prompt, response, thinking and tool payloads are recorded by default; that is the
point of the audit log. `Conversations.Enabled = false` turns recording off completely,
and `Conversations.MaxFieldChars` bounds individual fields. API keys are never written:
the `Authorization` header is not part of the captured payload, and a string value named
`api_key`, `apikey`, `authorization`, `token`, `secret` or `password` anywhere in the
captured body is replaced with `[masked]`.

### Consequences

- The conversation directory contains user content and must be secured at the filesystem
  level; it is git-ignored and documented as sensitive.
- Operators in regulated environments must set `Conversations.Enabled=false` or point
  `Conversations:Directory` at appropriately protected storage.
- The body mask is name-based, not content-based. A secret written into prose, or stored
  under a key outside the recognised list, is recorded as-is. Treat the log directory as
  holding cleartext secrets regardless.

---

## ADR-005 — The operator surface is loopback-only by default

**Date:** 2026-10-28
**Status:** Accepted

### Context

Adding the conversation audit log changed what sits behind the admin surface. Before it,
`/admin/*` exposed provider configuration and aggregate token counters. After it, the same
prefix serves the full text of every user prompt, every chain-of-thought block and every
tool result. The proxy has no inbound authentication at all — its `ApiKey` values are
outbound provider credentials — so there was no existing user model to hang an
`[Authorize]` check on.

### Decision

`AdminAccessMiddleware` refuses any request to the operator surface whose TCP peer address
is not loopback. The surface is `/admin/*`, `/api/providers*` and the three operator pages
(`/admin.html`, `/conversations.html`, `/conversation.html`). The proxied `/v1/*` API is
not gated. `Admin:RequireLocalhost` (default `true`) turns the restriction off.

### Alternatives considered

- **Full authentication** — the right long-term answer, but it needs a user model this proxy
  does not have and is a much larger change than the audit log itself.
- **Default `Conversations:Enabled=false` outside Development** — protects the content but
  quietly disables the feature for anyone running the binary without `ASPNETCORE_ENVIRONMENT`
  set, which is the normal way to run it.
- **Gate on `X-Forwarded-For`** — rejected outright. The client controls that header, so
  trusting it makes the restriction decorative.

### Consequences

- Remote operator access is gone unless deliberately re-enabled. Anyone fronting the proxy
  with a reverse proxy for operator use must put real authentication in front of it first,
  then set `Admin:RequireLocalhost=false`.
- The gate is a network boundary, not an identity check. Anything running on the same machine
  as the proxy can read every conversation.
- Adding real authentication later means replacing one middleware, and the operator surface
  already has a single place to hang it from.
