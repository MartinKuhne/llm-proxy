namespace LlmProxy.Domain;

/// <summary>
/// The kinds of event stored in a conversation audit log.
/// <list type="table">
///   <listheader><term>Kind</term><description>Meaning</description></listheader>
///   <item><term>request</term><description>An incoming chat-completion request with its raw body and message count.</description></item>
///   <item><term>thinking</term><description>Model reasoning (chain-of-thought) content reported by the provider.</description></item>
///   <item><term>response</term><description>The assistant answer with its finish reason and token usage.</description></item>
///   <item><term>tool_call</term><description>A tool invocation requested by the assistant.</description></item>
///   <item><term>tool_result</term><description>A tool result the client sent back in a follow-up request.</description></item>
///   <item><term>error</term><description>An upstream failure observed for the conversation.</description></item>
/// </list>
/// </summary>
public static class ConversationEventKind
{
    /// <summary>An incoming request from a client.</summary>
    public const string Request = "request";

    /// <summary>Reasoning / chain-of-thought content produced by the model.</summary>
    public const string Thinking = "thinking";

    /// <summary>The assistant answer.</summary>
    public const string Response = "response";

    /// <summary>A tool call requested by the assistant.</summary>
    public const string ToolCall = "tool_call";

    /// <summary>A tool result supplied by the client.</summary>
    public const string ToolResult = "tool_result";

    /// <summary>An upstream failure.</summary>
    public const string Error = "error";
}
