# LlmProxy Data Models

## Overview

This document describes the core data types and their relationships in the LlmProxy system.

---

## 1. Configuration Layer

### ProvidersConfig

The root configuration object containing all providers and settings.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Providers` | `IEnumerable<ProviderConfig>` | `[]` | List of configured providers |
| `CurrentProvider` | `string?` | `null` | Currently forced provider key |
| `RetryDelaySeconds` | `int` | `60` | Delay before retry on 503/429 |

**Source**: `Application/Configuration/ProvidersConfig.cs:8-11`

---

## 2. Provider Layer

### ProviderConfig

Represents a single LLM provider endpoint.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `Name` | `string` | Yes | Unique provider identifier |
| `BaseUrl` | `string` | Yes | API endpoint base URL |
| `ApiKey` | `string` | Yes | API authentication key |
| `Priority` | `int` | Yes | Selection priority (higher = preferred) |
| `Model` | `string` | Yes | Model identifier to use |
| `SendMaxCompletionTokens` | `bool` | No | Whether to send `max_completion_tokens` |
| `Cost` | `int` | No | Overall cost score (0-10) |
| `Complexity` | `int` | No | Overall complexity score (0-10) |
| `Description` | `string?` | No | Human-readable description |
| `Capabilities` | `Dictionary<string, CapabilityConfig>?` | No | Supported capabilities |
| `HasAllCapabilities` | `bool` | No | Whether provider supports all capabilities |
| `AllowAutomatic` | `bool` | No | Whether included in auto-selection |

**Key Computation**:
- Provider Key: `"{BaseUrl}|{Model}"` (Source: `ProviderManager.cs:107`)
- Full Model Key: `"{Name}:{Model}"` (Source: `ProviderManager.cs:38`)

### CapabilityConfig

Defines a specific capability offered by a provider.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Name` | `string` | - | Capability name (e.g., "chat", "embeddings") |
| `Cost` | `int` | `0` | Cost score (0-10) |
| `Complexity` | `int` | `0` | Complexity score (0-10) |
| `Supported` | `bool` | `false` | Whether capability is enabled |
| `MaxTokens` | `int?` | `null` | Maximum tokens for this capability |
| `MaxContexts` | `int?` | `null` | Maximum context length |
| `Limits` | `Dictionary<string, object>?` | `null` | Capability-specific limits |
| `Features` | `Dictionary<string, object>?` | `null` | Feature flags |

**Source**: `Domain/ProviderConfig.cs:1-8`

### ProviderState

Tracks runtime state for a provider (not persisted).

| Field | Type | Description |
|-------|------|-------------|
| `LastUsed` | `DateTime` | Last successful request timestamp |
| `LastError` | `DateTime?` | Last error timestamp |
| `ErrorCount` | `int` | Consecutive error count |
| `LastErrorMessage` | `string?` | Last error message |
| `LastException` | `string?` | Last exception details |
| `LastErrorStatusCode` | `int?` | Last HTTP status code |
| `LastErrorBody` | `string?` | Truncated response body |

**Source**: `Domain/ProviderState.cs:1-9`

---

## 3. Chat Types (OpenAI-Compatible)

### ChatMessage

| Field | Type | Description |
|-------|------|-------------|
| `Role` | `string` | "system", "user", "assistant", or "tool" |
| `Content` | `string` | Message content |

**Source**: `Domain/ChatMessage.cs:1-3`

### ChatCompletionRequest

| Field | Type | Description |
|-------|------|-------------|
| `Model` | `string` | Model identifier |
| `Messages` | `IEnumerable<ChatMessage>` | Conversation messages |
| `Stream` | `bool` | Whether to stream response |

**Source**: `Domain/ChatCompletionRequest.cs:1-5`

### ChatChoice

| Field | Type | Description |
|-------|------|-------------|
| `Index` | `int` | Choice index |
| `Message` | `ChatMessage` | Assistant's response message |
| `FinishReason` | `string` | Why generation stopped |

**Source**: `Domain/ChatChoice.cs:1-4`

### ChatCompletionResponse

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `string` | Unique response ID |
| `Object` | `string` | "chat.completion" |
| `Created` | `long` | Unix timestamp |
| `Model` | `string` | Model used |
| `Choices` | `IEnumerable<ChatChoice>` | Response choices |
| `Usage` | `Usage` | Token usage statistics |

**Source**: `Domain/ChatCompletionResponse.cs:1-7`

### Usage

| Field | Type | Description |
|-------|------|-------------|
| `PromptTokens` | `int` | Tokens in the prompt |
| `CompletionTokens` | `int` | Tokens in the completion |
| `TotalTokens` | `int` | Total tokens used |
| `CachedTokens` | `int` | Cached prompt tokens |

**Source**: `Domain/Usage.cs:1-5`

---

## 4. Logging Types

### TokenCountLog

Written to JSONL files for daily usage tracking.

| Field | Type | Description |
|-------|------|-------------|
| `Date` | `DateTime` | UTC timestamp |
| `Provider` | `string` | Provider name |
| `Model` | `string` | Model name |
| `PromptTokens` | `int` | Prompt tokens used |
| `CompletionTokens` | `int` | Completion tokens used |
| `TotalTokens` | `int` | Total tokens |
| `CachedTokens` | `int` | Cached tokens |
| `RequestCount` | `int` | Number of requests |
| `ElapsedMs` | `long` | Total elapsed time |

**Source**: `Domain/TokenCountLog.cs:1-11`

### ProviderUsageStat

Aggregated usage for a specific provider/model.

| Field | Type | Description |
|-------|------|-------------|
| `Provider` | `string` | Provider name |
| `Model` | `string` | Model name |
| `RequestCount` | `int` | Number of requests |
| `PromptTokens` | `int` | Total prompt tokens |
| `CompletionTokens` | `int` | Total completion tokens |
| `TotalTokens` | `int` | Total tokens |
| `CachedTokens` | `int` | Cached tokens |
| `TotalElapsedMs` | `long` | Total elapsed time |

**Source**: `Application/Services/ProviderUsageStat.cs:1-10`

### DailyUsageStat

Aggregated usage for a single day.

| Field | Type | Description |
|-------|------|-------------|
| `Date` | `DateOnly` | Date |
| `RequestCount` | `int` | Number of requests |
| `PromptTokens` | `int` | Total prompt tokens |
| `CompletionTokens` | `int` | Total completion tokens |
| `TotalTokens` | `int` | Total tokens |

**Source**: `Application/Services/IUsageReader.cs:14-19`

---

## 5. API DTOs

### ProviderInfoDto (Admin API)

| Field | Type | Description |
|-------|------|-------------|
| `Key` | `string` | Provider key (`BaseUrl\|Model`) |
| `FullModelKey` | `string` | Full key (`Name:Model`) |
| `Name` | `string` | Provider name |
| `BaseUrl` | `string` | Base URL |
| `Model` | `string` | Model |
| `Priority` | `int` | Priority |
| `IsForced` | `bool` | Whether currently forced |
| `IsAuto` | `bool` | Whether auto-selected |
| `ErrorCount` | `int` | Error count |
| `LastError` | `DateTime?` | Last error time |
| `LastErrorMessage` | `string?` | Last error message |
| `LastErrorStatusCode` | `int?` | Last HTTP status |
| `LastErrorBody` | `string?` | Last error body |
| `Cost` | `int` | Cost score |
| `Complexity` | `int` | Complexity score |
| `Description` | `string?` | Description |
| `Capabilities` | `Dictionary<string, CapabilityInfoDto>` | Capabilities |

**Source**: `Api/Responses/ProviderInfoDto.cs:1-22`

### ProvidersStatusDto

| Field | Type | Description |
|-------|------|-------------|
| `Providers` | `List<ProviderInfoDto>` | All providers |
| `IsForced` | `bool` | Whether any provider is forced |

**Source**: `Api/Responses/ProvidersStatusDto.cs:1-5`

### ProviderUsageStatDto

| Field | Type | Description |
|-------|------|-------------|
| `ProviderName` | `string` | Provider name |
| `Provider` | `string` | Provider (alias) |
| `Model` | `string` | Model |
| `RequestCount` | `int` | Requests |
| `PromptTokens` | `int` | Prompt tokens |
| `CompletionTokens` | `int` | Completion tokens |
| `TotalTokens` | `int` | Total tokens |
| `CachedTokens` | `int` | Cached tokens |
| `AvgPromptTokensPerSec` | `double` | Average speed |
| `AvgCompletionTokensPerSec` | `double` | Average speed |

**Source**: `Api/Responses/ProviderUsageStatDto.cs:1-14`

---

## 6. Class Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ProvidersConfig                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│ + Providers: IEnumerable<ProviderConfig>                                │
│ + CurrentProvider: string?                                              │
│ + RetryDelaySeconds: int = 60                                          │
├─────────────────────────────────────────────────────────────────────────────┤
│ + LoadFromJson(json: string): void                                       │
│ + ToJson(): string                                                      │
│ + Validate(): void                                                     │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ 1:N
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ProviderConfig                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│ + Name: string                                                          │
│ + BaseUrl: string                                                      │
│ + ApiKey: string                                                      │
│ + Priority: int                                                       │
│ + Model: string                                                       │
│ + SendMaxCompletionTokens: bool                                       │
│ + Cost: int = 0                                                       │
│ + Complexity: int = 0                                                 │
│ + Description: string?                                               │
│ + Capabilities: Dictionary<string, CapabilityConfig>?                  │
│ + HasAllCapabilities: bool = true                                      │
│ + AllowAutomatic: bool = true                                         │
└─────────────────────────────────────────────────────────────────��─��─────────┘
                                    │
                                    │ 0:N
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        CapabilityConfig                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│ + Name: string                                                          │
│ + Cost: int                                                            │
│ + Complexity: int                                                     │
│ + Supported: bool                                                     │
│ + MaxTokens: int?                                                      │
│ + MaxContexts: int?                                                    │
│ + Limits: Dictionary<string, object>?                               │
│ + Features: Dictionary<string, object>?                               │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                         ProviderState                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│ + LastUsed: DateTime                                                    │
│ + LastError: DateTime?                                                 │
│ + ErrorCount: int                                                     │
│ + LastErrorMessage: string?                                           │
│ + LastException: string?                                              │
│ + LastErrorStatusCode: int?                                           │
│ + LastErrorBody: string?                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 7. Relationship Summary

| Relationship | Type | Cardinality | Description |
|---------------|------|-------------|-------------|
| `ProvidersConfig → ProviderConfig` | Composition | 1:N | Config contains providers |
| `ProviderConfig → CapabilityConfig` | Composition | 0:N | Provider has capabilities |
| `ProviderManager → ProviderState` | Aggregation | 1:1 | State tracked per provider |
| `ProxyService → ProviderConfig` | Dependency | 1:N | Proxy uses providers |
| `FileTokenCountLogger → TokenCountLog` | Dependency | 1:N | Logger writes logs |
| `ChatCompletionResponse → Usage` | Composition | 1:1 | Response includes usage |
| `ChatChoice → ChatMessage` | Composition | 1:1 | Choice contains message |

---

## 8. Validation Rules

| Field | Rule | Error Message |
|-------|------|--------------|
| `ProviderConfig.Name` | NOT empty | "Provider has empty Name" |
| `ProviderConfig.BaseUrl` | NOT empty | "Provider 'X' has empty BaseUrl" |
| `ProviderConfig.Model` | NOT empty | "Provider 'X' has empty Model" |
| `ProviderConfig.Cost` | 0-10 | "Provider 'X' has invalid Cost. Must be between 0 and 10" |
| `ProviderConfig.Complexity` | 0-10 | "Provider 'X' has invalid Complexity. Must be between 0 and 10" |
| `CapabilityConfig.Cost` | 0-10 | "Provider 'X' has invalid Capability 'Y' Cost. Must be between 0 and 10" |
| `CapabilityConfig.Complexity` | 0-10 | "Provider 'X' has invalid Capability 'Y' Complexity. Must be between 0 and 10" |
| `CapabilityConfig.Name` | NOT "*" | "Provider 'X' has a '*' capability entry. Use 'HasAllCapabilities' instead." |