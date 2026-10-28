# Conversations API contract

Contract for the read side of the conversation audit log (increment 3) and the web UI
(increment 4). Written before implementation so both sides agree on shapes.

Storage side: one JSONL file per conversation under `<logs>/conversations/<id>.jsonl`,
plus an append-only `index.jsonl` roll-up. See ADR-001.

## `GET /admin/api/conversations`

Lists conversations, one entry per conversation, newest activity first.

| Query | Type | Default | Notes |
|-------|------|---------|-------|
| `from` | `yyyy-MM-dd` | none | Inclusive start date, UTC, compared against last activity date |
| `to` | `yyyy-MM-dd` | none | Inclusive end date, UTC |
| `page` | int ≥ 1 | `1` | 1-based page number |
| `pageSize` | int 1–200 | `50` | Entries per page |
| `provider` | string | none | Exact, case-insensitive provider filter |
| `q` | string | none | Case-insensitive substring match on title, id, provider, model |

```json
{
  "items": [
    {
      "id": "3f2a1c9b7e04d8a6",
      "title": "Refactor the auth middleware",
      "startedAt": "2026-10-27T09:14:02.311Z",
      "lastActivityAt": "2026-10-27T09:31:47.884Z",
      "turnCount": 12,
      "eventCount": 41,
      "provider": "OpenAI",
      "model": "gpt-4",
      "totalTokens": 18342,
      "hasErrors": false
    }
  ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 137,
  "totalPages": 3
}
```

Bad input (`page=0`, unparseable dates) is normalised to defaults rather than rejected.

## `GET /admin/api/conversations/{id}`

Full replay of one conversation.

```json
{
  "id": "3f2a1c9b7e04d8a6",
  "title": "Refactor the auth middleware",
  "startedAt": "2026-10-27T09:14:02.311Z",
  "lastActivityAt": "2026-10-27T09:31:47.884Z",
  "turnCount": 12,
  "eventCount": 41,
  "provider": "OpenAI",
  "model": "gpt-4",
  "totalTokens": 18342,
  "hasErrors": false,
  "events": [
    {
      "kind": "request",
      "timestamp": "2026-10-27T09:14:02.311Z",
      "requestId": "b1a2c3d4...",
      "provider": "OpenAI",
      "model": "gpt-4",
      "path": "/v1/chat/completions",
      "data": {
        "body": { "model": "gpt-4", "stream": true },
        "messages": [ "…the new messages only; body carries no messages key…" ],
        "messageCount": 7,
        "recordedCount": 5,
        "newMessageCount": 2,
        "lastMessageFingerprint": "0f3a1c9b7e04d8a6",
        "toolsFingerprint": "9b2e4d6f1a3c5e70"
      }
    },
    { "kind": "thinking",    "timestamp": "...", "requestId": "...", "data": { "content": "..." } },
    { "kind": "tool_call",   "timestamp": "...", "requestId": "...", "data": { "id": "call_1", "name": "read_file", "arguments": "{\"path\":\"a.cs\"}" } },
    { "kind": "tool_result", "timestamp": "...", "requestId": "...", "data": { "toolCallId": "call_1", "name": "read_file", "content": "..." } },
    { "kind": "response",    "timestamp": "...", "requestId": "...", "data": { "content": "...", "finishReason": "stop", "streamed": true, "usage": { } } },
    { "kind": "error",       "timestamp": "...", "requestId": "...", "data": { "statusCode": 429, "body": "..." } }
  ]
}
```

`404` when the conversation id is unknown or its file is missing.

> Timestamps are serialised by System.Text.Json with an explicit offset (`+00:00`) rather
> than the `Z` literal used in the examples above. Same instant, valid ISO-8601, and
> `new Date(...)` in the browser parses either.

## Rendering rules for the detail view

- Events arrive in file order, which is chronological per conversation; group them by
  `requestId` so each turn reads: prompt → thinking → tool calls → tool results → response.
- A `request` event carries **only the messages the log did not hold yet** (`data.messages`,
  mirrored into `data.body.messages`). Render them as they arrive; `role=tool` messages are
  skipped because the same turn already shows them as `tool_result` events.
- Log files written before the writer started slicing carry the whole resent history in
  `data.body.messages` and no `data.messages`. There the reader hides the messages an earlier
  turn already rendered (key each message by its canonical JSON and count occurrences) — the
  safety net that is no longer needed for current files, and one that must never be applied to
  `data.messages`, because there it would hide genuinely new messages.
- `thinking` is rendered visually de-emphasised from the answer; `error` is highlighted.

## Captured body shapes

The capture side (increment 2) normalises a few inputs before they reach `data.body` of a
`request` event, so the reader must not assume the body is always a chat-completion object:

- **Chat request** — the JSON body as received, except that `messages` holds only the new
  slice and unchanged `tools` are dropped (see below).
- **Multipart upload** (audio transcription and friends) — no binary is stored:
  `{ "multipart": true, "contentType": "...", "fields": { "model": "whisper-1", ... }, "files": [{ "name", "fileName", "contentType", "size" }] }`.
  The model then comes from `fields.model`.
- **Anything else, or JSON that did not parse** — `{ "raw": "<text>" }`.
- **Body larger than the 2 MB audit cap** — recorded truncated and flagged with
  `"auditTruncated": true` alongside the captured text.

### Dedupe of a resent prompt

OpenAI-style clients resend the whole conversation history on every turn, so turn N would
write N copies of the same messages. The writer stores each piece of content once:

| Field | Meaning |
|-------|---------|
| `body` | The request body with `messages` **removed** (they live in the `messages` field below) and `tools` dropped when already recorded; every other field is kept as it arrived, still subject to the field cap. |
| `messages` | The new messages only — the slice the log did not hold yet. Stored once, never duplicated inside `body`. |
| `messageCount` | How many messages the client actually sent this turn. |
| `recordedCount` | How many leading messages were omitted because they are already stored. |
| `newMessageCount` | Length of the stored slice. |
| `lastMessageFingerprint` | Fingerprint of the last message in the slice; `null` when the slice is empty. |
| `toolsFingerprint` | Fingerprint of the `tools` array — **only present when the tools were stored**. |

A fingerprint is the first 16 lowercase hex characters of the SHA-256 hash of the canonical
JSON of the value: object keys sorted recursively, array order preserved.

- **Skip rule.** `recordedCount` is the conversation's recorded message count when that count
  is within the resent history *and* the fingerprint of the message at that index still matches
  the recorded `lastMessageFingerprint`. Any mismatch — the client rewrote or branched its
  history — falls back to `recordedCount: 0` and stores the whole history rather than silently
  dropping messages.
- **Tools.** A `tools` array whose fingerprint equals the one already recorded is omitted from
  `body` and carries no `toolsFingerprint`; a changed array is stored together with its new
  fingerprint. Absence of `toolsFingerprint` therefore means "unchanged, already stored", not
  "no tools".
- **Tool results.** `tool_result` events are derived from the new slice only, so a tool output
  recorded at turn 2 is not re-emitted at turns 3, 4, 5…
- **Index.** `ConversationSummary` carries `recordedMessageCount`, `lastRecordedMessageFingerprint`
  and `recordedToolsFingerprint`, folded from the same request events by the live path and by a
  rebuild, so a rebuilt index reproduces the live dedupe state exactly.

A `response` event for an endpoint without an assistant message (embeddings and friends)
carries the whole upstream body as `data.raw` instead of `data.content`.

## Notes

- Content is user data. The endpoints sit under the admin surface; the log directory is
  git-ignored and must be protected at the filesystem level (ADR-004).
- Fields longer than `Conversations:MaxFieldChars` arrive already truncated with a
  trailing `...[truncated]` marker.
