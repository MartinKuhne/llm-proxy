namespace LlmProxy.Tests;

using System.Text;
using System.Text.Json;
using LlmProxy.Application;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.Extensions.Logging;
using NSubstitute;

[Collection("ConversationAuditLog")]
public class FileConversationLoggerTests : IDisposable
{
    private readonly string _directory;
    private readonly ConversationIndex _index;

    public FileConversationLoggerTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "llm-proxy-conversation-logger", Guid.NewGuid().ToString("N"));
        AppPaths.ConversationDirectory = _directory;
        _index = new ConversationIndex(Substitute.For<ILogger<ConversationIndex>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public async Task LogRequestAsync_WritesOneFilePerConversation()
    {
        var logger = CreateLogger();

        await logger.LogRequestAsync(Request("alpha", "hi"), CancellationToken.None);
        await logger.LogRequestAsync(Request("beta", "hello"), CancellationToken.None);

        Assert.Equal(
            new[] { "alpha.jsonl", "beta.jsonl" },
            ConversationFiles(SearchOption.TopDirectoryOnly).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task MultipleCalls_AppendToTheSameConversationFile()
    {
        var logger = CreateLogger();

        await logger.LogRequestAsync(Request("alpha", "hi"), CancellationToken.None);
        await logger.LogResponseAsync(Response("alpha", "thinking", "answer"), CancellationToken.None);
        await logger.LogFailureAsync(Failure("alpha"), CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(Path.Combine(_directory, "alpha.jsonl"), CancellationToken.None);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public async Task EveryLine_IsValidCamelCaseJson()
    {
        var logger = CreateLogger();

        await logger.LogRequestAsync(Request("alpha", "hi"), CancellationToken.None);
        await logger.LogResponseAsync(Response("alpha", "thinking", "answer"), CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(Path.Combine(_directory, "alpha.jsonl"), CancellationToken.None);
        Assert.Equal(3, lines.Length);

        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            Assert.Equal("alpha", root.GetProperty("conversationId").GetString());
            Assert.Equal("req-1", root.GetProperty("requestId").GetString());
            Assert.Equal("provider-a", root.GetProperty("provider").GetString());
            Assert.NotEqual(default, root.GetProperty("timestamp").GetDateTimeOffset());
            Assert.False(root.TryGetProperty("ConversationId", out _));
            Assert.False(root.TryGetProperty("Timestamp", out _));
        }
    }

    [Fact]
    public async Task DisabledOption_WritesNothing()
    {
        var logger = CreateLogger(new ConversationOptions { Enabled = false, Directory = _directory });

        await logger.LogRequestAsync(Request("alpha", "hi"), CancellationToken.None);
        await logger.LogResponseAsync(Response("alpha", "thinking", "answer"), CancellationToken.None);
        await logger.LogFailureAsync(Failure("alpha"), CancellationToken.None);

        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task UnsafeConversationId_CannotEscapeTheConversationDirectory()
    {
        var logger = CreateLogger();

        await logger.LogRequestAsync(Request("../../../evil", "hi"), CancellationToken.None);

        var file = Assert.Single(ConversationFiles(SearchOption.AllDirectories));
        var fileName = Path.GetFileName(file);

        Assert.StartsWith(Path.GetFullPath(_directory) + Path.DirectorySeparatorChar, Path.GetFullPath(file), StringComparison.Ordinal);
        Assert.DoesNotContain("..", fileName, StringComparison.Ordinal);
        Assert.DoesNotContain("/", fileName, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VeryLongConversationId_IsCappedInFileName()
    {
        var logger = CreateLogger();

        await logger.LogRequestAsync(Request(new string('c', 400), "hi"), CancellationToken.None);

        var file = Assert.Single(ConversationFiles(SearchOption.TopDirectoryOnly));
        Assert.Equal(100 + ".jsonl".Length, Path.GetFileName(file).Length);
    }

    [Fact]
    public async Task Logging_KeepsTheConversationIndexCurrent()
    {
        var logger = CreateLogger();

        await logger.LogRequestAsync(Request("alpha", "What is the weather?"), CancellationToken.None);
        await logger.LogResponseAsync(Response("alpha", "thinking", "answer"), CancellationToken.None);

        var summary = Assert.Single(_index.All());
        Assert.Equal("alpha", summary.Id);
        Assert.Equal(3, summary.EventCount);
        Assert.Equal(1, summary.TurnCount);
        Assert.Equal("provider-a", summary.Provider);
        Assert.Equal("gpt-test", summary.Model);
        Assert.Equal("What is the weather?", summary.Title);
        Assert.Equal(7, summary.TotalTokens);
    }

    [Fact]
    public void FileName_ReplacesUnsafeCharacters()
    {
        Assert.Equal("_", ConversationFileNames.FileName(null));
        Assert.Equal("_", ConversationFileNames.FileName(string.Empty));
        Assert.Equal("_", ConversationFileNames.FileName("."));
        Assert.Equal("._", ConversationFileNames.FileName(".."));
        Assert.Equal("a_b_c", ConversationFileNames.FileName("a/b\\c"));
        Assert.Equal("a_b", ConversationFileNames.FileName("a:b"));
        Assert.Equal("safe-id_1.x", ConversationFileNames.FileName("safe-id_1.x"));
        Assert.Equal("a._.b", ConversationFileNames.FileName("a...b"));
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData("..\\..\\evil")]
    [InlineData("....//....")]
    [InlineData("/")]
    [InlineData(".")]
    [InlineData("")]
    public void FileName_NeverProducesPathTraversal(string conversationId)
    {
        var name = ConversationFileNames.FileName(conversationId);

        Assert.DoesNotContain("..", name, StringComparison.Ordinal);
        Assert.DoesNotContain("/", name, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", name, StringComparison.Ordinal);
        Assert.NotEqual(string.Empty, name);
        Assert.True(name.Length <= 100);
    }

    [Fact]
    public async Task ResentHistory_EveryDistinctMessageIsWrittenExactlyOnce()
    {
        var logger = CreateLogger();

        var history = new List<string>
        {
            """{"role":"system","content":"You are a terse assistant."}""",
            """{"role":"user","content":"What is the weather in Berlin?"}""",
        };

        await logger.LogRequestAsync(ChatRequest("alpha", 1, history, Tools), CancellationToken.None);

        history.Add("""{"role":"assistant","content":"Calling the weather tool."}""");
        history.Add("""{"role":"tool","tool_call_id":"call_1","name":"get_weather","content":"18C and sunny in Berlin"}""");
        await logger.LogRequestAsync(ChatRequest("alpha", 2, history, Tools), CancellationToken.None);

        history.Add("""{"role":"user","content":"And tomorrow?"}""");
        await logger.LogRequestAsync(ChatRequest("alpha", 3, history, Tools), CancellationToken.None);

        history.Add("""{"role":"assistant","content":"Rain expected."}""");
        history.Add("""{"role":"user","content":"And the day after?"}""");
        await logger.LogRequestAsync(ChatRequest("alpha", 4, history, Tools), CancellationToken.None);

        var path = Path.Combine(_directory, "alpha.jsonl");
        var events = await ReadEventsAsync(path);
        var requests = events.Where(evt => evt.Kind == ConversationEventKind.Request).ToList();

        Assert.Equal(4, requests.Count);

        // Every message of the conversation is stored exactly once across the four resent turns.
        var stored = requests
            .SelectMany(evt => evt.Data!.Value.GetProperty("messages").EnumerateArray())
            .Select(message => message.GetRawText())
            .ToList();

        Assert.Equal(7, stored.Count);
        Assert.Equal(7, stored.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1, stored.Count(text => text.Contains("What is the weather in Berlin?", StringComparison.Ordinal)));
        Assert.Equal(1, stored.Count(text => text.Contains("You are a terse assistant.", StringComparison.Ordinal)));
        Assert.Equal(1, stored.Count(text => text.Contains("18C and sunny in Berlin", StringComparison.Ordinal)));
        Assert.Equal(1, stored.Count(text => text.Contains("And tomorrow?", StringComparison.Ordinal)));
        Assert.Equal(1, stored.Count(text => text.Contains("And the day after?", StringComparison.Ordinal)));

        // Only the turn that first carried the tools array stores them.
        Assert.Equal(1, requests.Count(evt => evt.Data!.Value.GetProperty("body").TryGetProperty("tools", out _)));

        // The tool message added in turn 2 is emitted once, not once per turn that resends it.
        Assert.Equal(1, events.Count(evt => evt.Kind == ConversationEventKind.ToolResult));

        // Raw view of the file: the first prompt text occurs exactly once. Before the fix the writer repeated it
        // in the body of every one of the four turns that resent it.
        var text = await File.ReadAllTextAsync(path, CancellationToken.None);
        Assert.Equal(1, Count(text, "What is the weather in Berlin?"));
        Assert.Equal(1, Count(text, "\"type\":\"function\""));
    }

    [Fact]
    public async Task RebuiltIndex_ReproducesTheLiveDedupeState()
    {
        var logger = CreateLogger();

        var history = new List<string> { """{"role":"user","content":"first"}""" };
        await logger.LogRequestAsync(ChatRequest("alpha", 1, history, Tools), CancellationToken.None);

        history.Add("""{"role":"assistant","content":"answer"}""");
        history.Add("""{"role":"tool","tool_call_id":"call_1","name":"get_weather","content":"tool output"}""");
        await logger.LogRequestAsync(ChatRequest("alpha", 2, history, Tools), CancellationToken.None);

        history.Add("""{"role":"user","content":"second"}""");
        await logger.LogRequestAsync(ChatRequest("alpha", 3, history, Tools), CancellationToken.None);

        var live = Assert.Single(_index.All());
        Assert.Equal(4, live.RecordedMessageCount);

        var rebuilt = new ConversationIndex(Substitute.For<ILogger<ConversationIndex>>());
        await rebuilt.RebuildAsync(CancellationToken.None);

        var summary = Assert.Single(rebuilt.All());
        Assert.Equal(live.RecordedMessageCount, summary.RecordedMessageCount);
        Assert.Equal(live.LastRecordedMessageFingerprint, summary.LastRecordedMessageFingerprint);
        Assert.Equal(live.RecordedToolsFingerprint, summary.RecordedToolsFingerprint);
    }

    private FileConversationLogger CreateLogger(ConversationOptions? options = null) =>
        new(Substitute.For<ILogger<FileConversationLogger>>(), _index, options ?? new ConversationOptions { Directory = _directory });

    private const string Tools = """[{"type":"function","function":{"name":"get_weather","parameters":{"type":"object"}}}]""";

    private static async Task<IReadOnlyList<ConversationEvent>> ReadEventsAsync(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var lines = await File.ReadAllLinesAsync(path, CancellationToken.None);

        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<ConversationEvent>(line, options)!)
            .ToList();
    }

    /// <summary>Number of times <paramref name="needle"/> occurs in <paramref name="haystack"/>.</summary>
    private static int Count(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>A chat-completion request that resends the whole conversation history built so far.</summary>
    private static ConversationRequest ChatRequest(string conversationId, int turn, IReadOnlyList<string> messages, string? tools = null)
    {
        var body = new StringBuilder("""{"model":"gpt-test","messages":[""");
        body.Append(string.Join(',', messages)).Append(']');

        if (tools is not null)
        {
            body.Append(",\"tools\":").Append(tools);
        }

        body.Append('}');

        return new ConversationRequest(
            conversationId,
            $"req-{turn}",
            DateTimeOffset.UtcNow,
            "provider-a",
            "gpt-test",
            "/v1/chat/completions",
            Json(body.ToString()));
    }

    /// <summary>Every conversation file written so far, excluding the index file.</summary>
    private string[] ConversationFiles(SearchOption searchOption) =>
        Directory
            .GetFiles(_directory, "*.jsonl", searchOption)
            .Where(file => !string.Equals(Path.GetFileName(file), ConversationIndex.IndexFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static ConversationRequest Request(string conversationId, string userContent) =>
        new(
            conversationId,
            "req-1",
            DateTimeOffset.UtcNow,
            "provider-a",
            "gpt-test",
            "/v1/chat/completions",
            Json($$"""{"model":"gpt-test","messages":[{"role":"user","content":{{JsonSerializer.Serialize(userContent)}}}]}"""));

    private static ConversationResponse Response(string conversationId, string reasoning, string content) =>
        new(
            conversationId,
            "req-1",
            DateTimeOffset.UtcNow,
            "provider-a",
            "gpt-test",
            "/v1/chat/completions",
            Json($$"""{"role":"assistant","reasoning":{{JsonSerializer.Serialize(reasoning)}},"content":{{JsonSerializer.Serialize(content)}},"finish_reason":"stop"}"""),
            Json("""{"total_tokens":7}"""),
            false);

    private static ConversationFailure Failure(string conversationId) =>
        new(conversationId, "req-1", DateTimeOffset.UtcNow, "provider-a", "gpt-test", "/v1/chat/completions", 503, "no capacity");

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
