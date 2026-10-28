[ ] Create an OpenAI compatible REST API endpoint
[ ] Build into a docker container
[ ] Provide a docker compose file
[ ] Read a set of destination URLs, API keys, priorities and model names from a JSON file
[ ] Implement capability-based model routing (chat, embeddings, transcriptions, etc.)
[ ] Allow providers to specify supported capabilities with cost/complexity metrics
[ ] Support * wildcard for providers that support all capabilities
[ ] Return 503 Service Unavailable when no provider is available for requested capability
[ ] Forward requests to the destination URL and return the response to the caller
[ ] If there is an error, switch to the next provider in order of priority that supports the capability
[ ] Semantic logging with Serilog
[ ] Write a daily jsonL log with the token counts that are included with the final chat response

## Conversation audit log

- [x] Record every prompt, response, thinking block, tool call, tool result and failure
- [x] Store one JSONL file per conversation, plus an append-only index for cheap listing
- [x] Resolve conversation identity from `X-Conversation-Id`, else hash the opening messages
- [x] Fold streaming deltas into one assistant message (content, reasoning, parallel tool calls, usage)
- [x] REST API: `GET /admin/api/conversations` with date filter and paging
- [x] REST API: `GET /admin/api/conversations/{id}` returning the full event trail
- [x] Web UI: conversation list with date filter and paging, one row per conversation
- [x] Web UI: detail view replaying the whole conversation
- [ ] Store only the new messages on each `request` event. Today the client's whole resent
      history is stored every turn, so a long conversation file grows quadratically. The
      detail view already renders only the new ones; the writer should stop storing the rest.
- [ ] Compact `index.jsonl`. It takes one line per recorded turn-phase and is never
      rewritten, so it outgrows the conversations it describes over time.
- [ ] Retention policy, e.g. `Conversations:RetentionDays`, with a startup sweep.
- [ ] Return the resolved `X-Conversation-Id` on the proxy response so a client that did
      not send one can still correlate its later turns with the recorded conversation.
- [ ] Redaction rules for known-secret shapes (API keys, bearer tokens) appearing inside
      prompt or tool payload content, not just in headers.