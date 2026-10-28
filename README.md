# llm-proxy

A smart LLM proxy that automatically routes requests to the best available provider based on priority, cost, and capability requirements.

## Features

- **Automatic Failover**: Routes requests to the next available provider when the current one fails
- **Capability-Based Routing**: Routes requests to providers based on their supported capabilities (chat, embeddings, etc.)
- **Cost & Complexity Metrics**: Each provider has cost and complexity scores (0-10) for intelligent routing
- **Priority-Based Selection**: Providers are selected in priority order
- **Usage Tracking**: Logs token usage daily in JSONL format
- **Conversation Audit Log**: Records every prompt, response, thinking block, tool call, tool result and failure — one file per conversation
- **Admin Dashboard**: Web UI for monitoring provider status, switching providers and replaying conversations

## Configuration

Configuration is managed via `providers.json` in the application root directory.

### Provider File Format

```json
{
  "Providers": [
    {
      "Name": "OpenAI",
      "BaseUrl": "https://api.openai.com/v1",
      "ApiKey": "your-api-key",
      "Priority": 1,
      "Model": "gpt-4",
      "SendMaxCompletionTokens": true,
      "Cost": 8,
      "Complexity": 10,
      "Capabilities": {
        "chat": { "Cost": 8, "Complexity": 10, "Supported": true },
        "embeddings": { "Cost": 2, "Complexity": 3, "Supported": true }
      },
      "Description": "Primary provider - OpenAI GPT models"
    },
    {
      "Name": "Universal",
      "BaseUrl": "https://api.example.com/v1",
      "ApiKey": "",
      "Priority": 99,
      "Model": "universal-model",
      "Capabilities": "*",
      "Description": "Fallback provider with all capabilities"
    }
  ],
  "CurrentProvider": null
}
```

### Provider Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `Name` | string | Yes | - | Unique provider identifier |
| `BaseUrl` | string | Yes | - | API endpoint base URL |
| `ApiKey` | string | Yes | - | API authentication key |
| `Priority` | integer | Yes | - | Selection priority (higher = preferred) |
| `Model` | string | Yes | - | Model identifier to use |
| `SendMaxCompletionTokens` | boolean | No | false | Whether to send max_completion_tokens |
| `Cost` | integer | No | 0 | Overall cost score (0-10) |
| `Complexity` | integer | No | 0 | Overall complexity score (0-10) |
| `Capabilities` | object/string | No | "*" | Supported capabilities |
| `Description` | string | No | null | Human-readable description |

### Capabilities

Capabilities define what operations a provider supports. Each capability can have its own cost and complexity metrics.

#### Capability Structure

```json
{
  "chat": {
    "Cost": 5,
    "Complexity": 7,
    "Supported": true,
    "MaxTokens": 4096,
    "MaxContexts": 128000,
    "Limits": {
      "max_requests_per_minute": 60
    },
    "Features": {
      "streaming": true
    }
  }
}
```

#### Capability Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `Name` | string | Yes | - | Capability name (e.g., "chat", "embeddings") |
| `Cost` | integer | No | 0 | Cost score for this capability (0-10) |
| `Complexity` | integer | No | 0 | Complexity score (0-10) |
| `Supported` | boolean | No | false | Whether this capability is enabled |
| `MaxTokens` | integer | No | null | Maximum tokens for this capability |
| `MaxContexts` | integer | No | null | Maximum context length |
| `Limits` | object | No | null | Capability-specific limits |
| `Features` | object | No | null | Feature flags |

#### Common Capabilities

Based on the vLLM OpenAI-compatible API, common capabilities include:

| Capability | Description |
|-----------|-------------|
| `chat` | Chat completions API (`/v1/chat/completions`) |
| `completions` | Text completion API (`/v1/completions`) |
| `embeddings` | Embeddings API (`/v1/embeddings`) |
| `transcriptions` | Audio transcription API (`/v1/audio/transcriptions`) |
| `translations` | Audio translation API (`/v1/audio/translations`) |
| `pooling` | Pooling API (`/pooling`) |
| `classify` | Classification API (`/classify`) |
| `score` | Scoring API (`/score`) |
| `rerank` | Reranking API (`/rerank`) |
| `tokenize` | Tokenization API (`/tokenize`, `/detokenize`) |

### Wildcard Capabilities

Use `"Capabilities": "*"` to indicate a provider supports all capabilities:

```json
{
  "Name": "Universal",
  "BaseUrl": "https://api.example.com/v1",
  "ApiKey": "key",
  "Priority": 99,
  "Model": "universal",
  "Capabilities": "*",
  "Description": "Fallback provider"
}
```

When using the wildcard:
- `ProviderSupportsCapability()` returns `true` for any capability
- `GetCapabilityConfig()` returns the provider's default Cost and Complexity

### Provider Selection

Providers are selected based on:

1. **Forced Provider**: If a provider is manually forced, it's always used
2. **Capability Match**: For capability-specific requests, only providers supporting that capability are considered
3. **Error Status**: Providers with recent errors (< 1 hour) are skipped
4. **Priority**: Among available providers, the highest priority is selected

### Cost and Complexity Scoring

Both cost and complexity use a 0-10 scale:

- **Cost**: Monetary/performance cost (0 = free/fast, 10 = expensive/slow)
- **Complexity**: Task complexity the provider can handle (0 = simple, 10 = complex)

When routing, you can specify optional constraints:
```csharp
providerManager.GetProviderForCapability("chat", maxCost: 5, maxComplexity: 8);
```

### Examples

#### Simple Chat Provider
```json
{
  "Name": "GPT-4",
  "BaseUrl": "https://api.openai.com/v1",
  "ApiKey": "sk-...",
  "Priority": 1,
  "Model": "gpt-4",
  "Cost": 8,
  "Complexity": 10,
  "Capabilities": "*"
}
```

#### Specialized Embedding Provider
```json
{
  "Name": "EmbeddingService",
  "BaseUrl": "https://api.example.com/v1",
  "ApiKey": "key",
  "Priority": 2,
  "Model": "embedding-model",
  "Cost": 2,
  "Complexity": 3,
  "Capabilities": {
    "embeddings": {
      "Cost": 2,
      "Complexity": 3,
      "Supported": true
    }
  }
}
```

#### Transcription Service
```json
{
  "Name": "Whisper",
  "BaseUrl": "https://api.openai.com/v1",
  "ApiKey": "key",
  "Priority": 3,
  "Model": "whisper-1",
  "Cost": 4,
  "Complexity": 6,
  "Capabilities": {
    "transcriptions": {
      "Cost": 4,
      "Complexity": 6,
      "Supported": true
    }
  }
}
```

## API Endpoints

### Admin Dashboard
- `GET /admin` - Web UI for provider management

### Provider Management
- `GET /admin/api/providers` - List all providers with status
- `POST /admin/api/providers/active` - Set forced provider
- `DELETE /admin/api/providers/active` - Reset to auto-selection

### Usage
- `GET /admin/api/usage/today` - Get today's token usage statistics

### Conversations
- `GET /admin/api/conversations` - List conversations with date filter and paging
- `GET /admin/api/conversations/{id}` - Get one conversation with its full event trail

See [docs/conversations-api.md](docs/conversations-api.md) for the full request and response contract.

### Proxy
- `POST /v1/chat/completions` - Forward chat completion requests
- `POST /v1/completions` - Forward completion requests
- `POST /v1/embeddings` - Forward embedding requests

## Conversation Audit Log

Every proxied call is recorded so a conversation can be replayed later. The trail is
plain JSONL — grep it, back it up, ship it elsewhere.

### Storage

| Path | Contents |
|------|----------|
| `<logs>/conversations/<conversation-id>.jsonl` | One file per conversation, one event per line |
| `<logs>/conversations/index.jsonl` | Append-only roll-up that makes listing cheap; delete it and the index is rebuilt from the conversation files |

Event kinds: `request`, `thinking`, `response`, `tool_call`, `tool_result`, `error`.
The `request` event carries the whole inbound body; the assistant side is decomposed so
reasoning and each tool call are individually visible. Streaming responses are folded back
into one assistant message, so a streamed turn looks the same as a buffered one.

### Conversation identity

1. The `X-Conversation-Id` request header is used verbatim when the client sends one.
2. Otherwise the id is derived from the conversation opening — SHA-256 over the first
   system message plus the first user message — so the multi-turn history an OpenAI client
   resends each turn keeps landing in the same file.
3. With neither, the request becomes its own conversation.

Each HTTP turn also gets a fresh request id, which is what groups events inside a file.

### Configuration

```json
{
  "Conversations": {
    "Enabled": true,
    "Directory": "D:/llm-proxy/conversations",
    "MaxFieldChars": 100000
  }
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `Conversations:Enabled` | `true` | Kill switch — turns the audit log off completely |
| `Conversations:Directory` | `<logs>/conversations` | Where the files live |
| `Conversations:MaxFieldChars` | `100000` | Any single string field longer than this is stored truncated |

### Privacy

The log contains user content. The directory is git-ignored and must be protected at the
filesystem level. API keys are never written: the `Authorization` header is not part of
the captured payload, and multipart uploads are recorded as a field and file summary
rather than as binary. Set `Conversations:Enabled=false` if you must not retain content.

## Running

```bash
dotnet run
```

The application will start on `http://localhost:5000` by default.

## Environment Variables

| Variable | Description |
|----------|-------------|
| `ASPNETCORE_URLS` | URLs to bind to (default: `http://localhost:5000`) |
| `Logging__LogLevel__Default` | Logging level |
| `Conversations__Enabled` | Set to `false` to disable the conversation audit log |
| `Conversations__Directory` | Where conversation files are written |
| `Conversations__MaxFieldChars` | Truncate captured string fields beyond this size |

## License

[MIT](./LICENSE.md)
