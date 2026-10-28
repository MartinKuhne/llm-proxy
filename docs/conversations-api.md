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
      "data": { "body": { "messages": ["..."] }, "messageCount": 1 }
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

## Rendering rules for the detail view

- Events arrive in file order, which is chronological per conversation; group them by
  `requestId` so each turn reads: prompt → thinking → tool calls → tool results → response.
- A `request` event carries the whole resent history. Render only the messages that were
  not already rendered for an earlier turn of the same conversation (history is
  append-only), and expose the untouched body as a collapsible raw view.
- `thinking` is rendered visually de-emphasised from the answer; `error` is highlighted.

## Captured body shapes

The capture side (increment 2) normalises a few inputs before they reach `data.body` of a
`request` event, so the reader must not assume the body is always a chat-completion object:

- **Chat request** — the JSON body exactly as received.
- **Multipart upload** (audio transcription and friends) — no binary is stored:
  `{ "multipart": true, "contentType": "...", "fields": { "model": "whisper-1", ... }, "files": [{ "name", "fileName", "contentType", "size" }] }`.
  The model then comes from `fields.model`.
- **Anything else, or JSON that did not parse** — `{ "raw": "<text>" }`.
- **Body larger than the 2 MB audit cap** — recorded truncated and flagged with
  `"auditTruncated": true` alongside the captured text.

A `response` event for an endpoint without an assistant message (embeddings and friends)
carries the whole upstream body as `data.raw` instead of `data.content`.

## Notes

- Content is user data. The endpoints sit under the admin surface; the log directory is
  git-ignored and must be protected at the filesystem level (ADR-004).
- Fields longer than `Conversations:MaxFieldChars` arrive already truncated with a
  trailing `...[truncated]` marker.
