namespace LlmProxy.Tests;

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
    public void SafeFileName_ReplacesUnsafeCharacters()
    {
        Assert.Equal("_", FileConversationLogger.SafeFileName(null));
        Assert.Equal("_", FileConversationLogger.SafeFileName(string.Empty));
        Assert.Equal("_", FileConversationLogger.SafeFileName("."));
        Assert.Equal("._", FileConversationLogger.SafeFileName(".."));
        Assert.Equal("a_b_c", FileConversationLogger.SafeFileName("a/b\\c"));
        Assert.Equal("a_b", FileConversationLogger.SafeFileName("a:b"));
        Assert.Equal("safe-id_1.x", FileConversationLogger.SafeFileName("safe-id_1.x"));
        Assert.Equal("a._.b", FileConversationLogger.SafeFileName("a...b"));
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData("..\\..\\evil")]
    [InlineData("....//....")]
    [InlineData("/")]
    [InlineData(".")]
    [InlineData("")]
    public void SafeFileName_NeverProducesPathTraversal(string conversationId)
    {
        var name = FileConversationLogger.SafeFileName(conversationId);

        Assert.DoesNotContain("..", name, StringComparison.Ordinal);
        Assert.DoesNotContain("/", name, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", name, StringComparison.Ordinal);
        Assert.NotEqual(string.Empty, name);
        Assert.True(name.Length <= 100);
    }

    private FileConversationLogger CreateLogger(ConversationOptions? options = null) =>
        new(Substitute.For<ILogger<FileConversationLogger>>(), _index, options ?? new ConversationOptions { Directory = _directory });

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
