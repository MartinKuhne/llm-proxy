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
- [x] Store only the new messages on each `request` event: the body keeps the request fields
      with `messages` removed and the new slice goes in `data.messages`, so a long
      conversation no longer grows quadratically.
- [x] Make one conversation's write atomic, and only per conversation: the dedupe snapshot is
      read inside a per-conversation lock, so concurrent turns of one conversation cannot
      store the same messages twice while different conversations stay parallel.
- [x] Validate `X-Conversation-Id` against `[A-Za-z0-9._-]{1,100}` and ignore anything else,
      so a client cannot name a conversation it does not own.
- [x] Mask string values named `api_key`, `apikey`, `authorization`, `token`, `secret` or
      `password` anywhere in the captured body.
- [ ] Compact `index.jsonl`. It takes one line per recorded turn-phase and is never
      rewritten, so it outgrows the conversations it describes over time.
- [ ] Retention policy, e.g. `Conversations:RetentionDays`, with a startup sweep.
- [ ] Return the resolved `X-Conversation-Id` on the proxy response so a client that did
      not send one can still correlate its later turns with the recorded conversation.
- [ ] Redact known-secret *shapes* (API keys, bearer tokens) inside prompt and tool payload
      content. The first pass masks by field name only; a secret embedded in prose or under an
      unrecognised key still gets through.

### Open, pending a decision

- [ ] **H5** Unauthenticated `/admin/*` read surface: the conversation list and replay
      endpoints expose every recorded prompt and response to anyone who can reach the port.
      Blocked on a decision about the deployment posture before changing
      `Conversations:Enabled` defaults or adding auth middleware.
- [ ] **H4** Mix a per-client discriminator into the derived conversation id, so two clients
      with an identical opening stop landing in one file. Blocked on the same decision as H5.

### Open, known limitations

- [ ] **M1** Chained prefix fingerprints: only the boundary message is verified, so an edit
      to an earlier message with an unchanged tail is silently not recorded.
- [ ] **M2** Aggregate payload budget: `MaxFieldChars` bounds each string leaf, not the total
      size of a node, so many medium fields can still add up to a very large event.
- [ ] **M7** Unbounded `StringBuilder` growth in `SseAssistantAssembler` â€” a long-lived or
      hostile stream can grow it without a ceiling.
- [ ] **L2** Serialisation churn: request bodies are parsed, re-serialised and re-parsed
      between capture, factory and writer.
- [ ] **L5** No caching of derived values (fingerprints, file paths) across turns that reuse
      them.
- [ ] **L6** `SetIfMissing` treats an empty string as "missing", so an explicit empty value
      can be overwritten later.
- [ ] **L10** `AppPaths.ConversationDirectory` is process-wide mutable static state injected
      at startup rather than passed in.
- [ ] **L11** Buffer copies on the audit read path that could be avoided with spans or pooled
      buffers.
- [ ] **I1** `UseStaticFiles` ordering: the operator pages are served before the API
      middleware, so the ordering is incidental rather than deliberate.
- [ ] **I2** Cancelled-request audit gap: a request cancelled mid-flight leaves the request
      event without its response, so the trail does not say what happened.
