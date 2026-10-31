# Entity Relationship Diagram

## Core Entities

```mermaid
erDiagram
    ProvidersConfig ||--o{ ProviderConfig : "1:N"
    ProviderConfig ||--o{ CapabilityConfig : "0:N"
    ProviderState }o--|| ProviderConfig : "1:1 tracked"
    
    TokenCountLog }o--|| ProviderConfig : "references"
    
    IProxyService }o--|| IProviderManager : uses
    IProxyService }o--|| ITokenCountLogger : uses
    IProxyService }o--|| IHttpClientFactory : uses
    
    IProviderManager ||--|| ProviderManager : implements
    ITokenCountLogger ||--|| FileTokenCountLogger : implements
    IUsageReader ||--|| FileUsageReader : implements
    
    AdminController }o--|| IProviderManager : injects
    AdminController }o--|| IUsageReader : injects
    ProvidersController }o--|| IProviderManager : injects
    ProxyController }o--|| IProxyService : injects
    
    ChatCompletionRequest ||--o{ ChatMessage : "1:N"
    ChatCompletionResponse ||--o{ ChatChoice : "1:N"
    ChatChoice ||--|| ChatMessage : "1:1"
    ChatCompletionResponse ||--|| Usage : "1:1"
```

## Domain Types

```mermaid
erDiagram
    ProvidersConfig {
        IEnumerable_ProviderConfig_ProviderList Providers
        string CurrentProvider
        int RetryDelaySeconds 60
    }

    ProviderConfig {
        string Name
        string BaseUrl
        string ApiKey
        int Priority
        string Model
        bool SendMaxCompletionTokens
        int Cost
        int Complexity
        string Description
        Dictionary_CapabilityConfig_Capabilities
        bool HasAllCapabilities
        bool AllowAutomatic
        bool AutoDiscovered
    }

    CapabilityConfig {
        string Name
        int Cost
        int Complexity
        bool Supported
        int_MaxTokens
        int_MaxContexts
        Dictionary_string_object_Limits
        Dictionary_string_object_Features
    }

    ProviderState {
        DateTime LastUsed
        datetime LastError
        int ErrorCount
        string LastErrorMessage
        string LastException
        int LastErrorStatusCode
        string LastErrorBody
    }

    TokenCountLog {
        date Date
        string Provider
        string Model
        int PromptTokens
        int CompletionTokens
        int TotalTokens
        int CachedTokens
        int RequestCount
        long ElapsedMs
    }

    ChatMessage {
        string Role
        string Content
    }

    ChatChoice {
        int Index
        ChatMessage Message
        string FinishReason
    }

    ChatCompletionRequest {
        string Model
        List_ChatMessage Messages
        bool Stream
    }

    ChatCompletionResponse {
        string Id
        string Object
        long Created
        string Model
        List_ChatChoice Choices
        Usage Usage
    }

    Usage {
        int PromptTokens
        int CompletionTokens
        int TotalTokens
        int CachedTokens
    }

    ProviderUsageStat {
        string Provider
        string Model
        int RequestCount
        int PromptTokens
        int CompletionTokens
        int TotalTokens
        int CachedTokens
        long TotalElapsedMs
    }

    DailyUsageStat {
        dateonly Date
        int RequestCount
        int PromptTokens
        int CompletionTokens
        int TotalTokens
    }
```

## Service Dependencies

```mermaid
graph TD
    subgraph Controllers
        Admin[AdminController]
        Providers[ProvidersController]
        Proxy[ProxyController]
        Models[ModelsController]
    end
    
    subgraph Services
        IProxy[IProxyService]
        IPM[IProviderManager]
        ITCL[ITokenCountLogger]
        IUR[IUsageReader]
    end
    
    subgraph Implementations
        PS[ProxyService]
        PM[ProviderManager]
        FTCL[FileTokenCountLogger]
        FUR[FileUsageReader]
    end
    
    Admin --> IPM
    Admin --> IUR
    Providers --> IPM
    Proxy --> IProxy
    Models --> IPM
    
    IProxy --> PS
    IPM --> PM
    ITCL --> FTCL
    IUR --> FUR
    
    PS --> IPM
    PS --> ITCL
    PS --> IHttpClientFactory
```

## Request Flow

```mermaid
sequenceDiagram
    participant Client
    participant ProxyController
    participant ProxyService
    participant ProviderManager
    participant Provider
    participant Upstream

    Client->>ProxyController: POST /v1/chat/completions
    ProxyController->>ProxyService: ProxyAsync(context, "chat/completions")
    
    ProxyService->>ProviderManager: GetBestProvider()
    ProviderManager-->>ProxyService: Provider
    
    loop For each provider (best first)
        ProxyService->>Provider: Build upstream URL
        ProxyService->>Provider: Substitute model
        ProxyService->>Provider: Replace Authorization
        ProxyService->>Upstream: Forward request
        
        alt Success (2xx)
            Upstream-->>ProxyService: Response + Usage
            ProxyService->>ProviderManager: RecordSuccess()
            ProxyService->>FileTokenCountLogger: LogTokenCountAsync()
            ProxyService-->>Client: Forwarded response
        end
        
        alt Failure (non-2xx) & retryable (503/429)
            ProxyService->>ProviderManager: RecordError()
            ProxyService->>Provider: Wait RetryDelaySeconds
        end
        
        alt Failure (non-retryable)
            ProxyService->>ProviderManager: RecordError()
        end
    end
    
    alt All providers failed
        ProxyService-->>Client: 502 Bad Gateway
    end
```

## Provider Selection Logic

```mermaid
flowchart TD
    A[Start: Get Provider] --> B{Forced Provider?}
    B -->|Yes| C[Return forced provider]
    B -->|No| D[Get all providers]
    D --> E[Filter: error count == 0<br/>OR last error > 1 hour ago]
    E --> F[Sort by Priority DESC]
    F --> G[Return first provider]
    
    H[Get Provider for Capability] --> I{Forced?}
    I -->|Yes| J[Return forced if capable]
    I -->|No| K[Get capable providers]
    K --> L[Filter by cost/complexity]
    L --> M[Sort by Priority DESC]
    M --> N[Return first capable]
```

## Configuration Loading

```mermaid
flowchart TD
    A[Start] --> B{PROVIDERS_FILE env var?}
    B -->|Set| C[Use env var path]
    B -->|Not set| D{ContentRoot/providers.json exists?}
    D -->|Yes| C
    D -->|No| E(~/.config/llm-proxy/providers.json exists?)
    E -->|Yes| F[Use user config path]
    E -->|No| G[Use default empty config]
    
    C --> H[Load JSON]
    F --> H
    G --> H
    H --> I[Parse ProvidersConfig]
    I --> J[Validate()]
    J --> K[Ready]
```