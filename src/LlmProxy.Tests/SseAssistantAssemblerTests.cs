namespace LlmProxy.Tests;

using System.Text.Json;
using LlmProxy.Application.Services;

public class SseAssistantAssemblerTests
{
    [Fact]
    public void Assembler_StartsEmpty()
    {
        var assembler = new SseAssistantAssembler();

        Assert.False(assembler.HasContent);
        Assert.Null(assembler.AssistantMessage);
        Assert.Null(assembler.Usage);
    }

    [Fact]
    public void Append_ConcatenatesContentFragmentsInOrder()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[{"index":0,"delta":{"role":"assistant","content":"Hel"}}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":{"content":"lo the"}}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":{"content":"re"}}]}""");

        var message = assembler.AssistantMessage!.Value;
        Assert.True(assembler.HasContent);
        Assert.Equal("assistant", message.GetProperty("role").GetString());
        Assert.Equal("Hello there", message.GetProperty("content").GetString());
    }

    [Fact]
    public void Append_CollectsReasoningContentIntoReasoning()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[{"index":0,"delta":{"reasoning_content":"think "}}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":{"reasoning_content":"ing"}}]}""");

        Assert.Equal("think ing", assembler.AssistantMessage!.Value.GetProperty("reasoning").GetString());
    }

    [Fact]
    public void Append_CollectsReasoningIntoReasoning()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[{"index":0,"delta":{"reasoning":"deep "}}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":{"reasoning":"thoughts"}}]}""");

        Assert.Equal("deep thoughts", assembler.AssistantMessage!.Value.GetProperty("reasoning").GetString());
    }

    [Fact]
    public void Append_BothReasoningSpellingsLandInOneReasoningField()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[{"index":0,"delta":{"reasoning":"a"}}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":{"reasoning_content":"b"}}]}""");

        var message = assembler.AssistantMessage!.Value;
        Assert.Equal("ab", message.GetProperty("reasoning").GetString());
        Assert.False(message.TryGetProperty("reasoning_content", out _));
    }

    [Fact]
    public void Append_MergesToolCallFragmentsByIndex()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"get_weather","arguments":""}}]}}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"city\":"}}]}}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"Berlin\"}"}}]},"finish_reason":"tool_calls"}]}""");

        var call = assembler.AssistantMessage!.Value.GetProperty("tool_calls")[0];
        Assert.Equal("call_1", call.GetProperty("id").GetString());
        Assert.Equal("function", call.GetProperty("type").GetString());
        Assert.Equal("get_weather", call.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("""{"city":"Berlin"}""", call.GetProperty("function").GetProperty("arguments").GetString());
    }

    [Fact]
    public void Append_KeepsParallelToolCallsSeparate()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append(
            """
            {"choices":[{"index":0,"delta":{"tool_calls":[
              {"index":0,"id":"call_a","type":"function","function":{"name":"first","arguments":"{"}},
              {"index":1,"id":"call_b","type":"function","function":{"name":"second","arguments":"["}}
            ]}}]}
            """);
        assembler.Append(
            """
            {"choices":[{"index":0,"delta":{"tool_calls":[
              {"index":0,"function":{"arguments":"\"x\":1}"}},
              {"index":1,"function":{"arguments":"\"y\":2]"}}
            ]}}]}
            """);

        var calls = assembler.AssistantMessage!.Value.GetProperty("tool_calls");
        Assert.Equal(2, calls.GetArrayLength());
        Assert.Equal("call_a", calls[0].GetProperty("id").GetString());
        Assert.Equal("""{"x":1}""", calls[0].GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal("call_b", calls[1].GetProperty("id").GetString());
        Assert.Equal("""["y":2]""", calls[1].GetProperty("function").GetProperty("arguments").GetString());
    }

    [Fact]
    public void Append_ToolsWithoutExplicitIndexAreMergedByPosition()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[{"index":0,"delta":{"tool_calls":[{"id":"call_1","function":{"name":"go"}}]}}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":{"tool_calls":[{"function":{"arguments":"{}"}}]}}]}""");

        var calls = assembler.AssistantMessage!.Value.GetProperty("tool_calls");
        Assert.Equal(1, calls.GetArrayLength());
        Assert.Equal("call_1", calls[0].GetProperty("id").GetString());
        Assert.Equal("go", calls[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("{}", calls[0].GetProperty("function").GetProperty("arguments").GetString());
    }

    [Fact]
    public void Append_TakesLastNonNullFinishReason()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[{"index":0,"delta":{"content":"a"},"finish_reason":null}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":{"content":"b"},"finish_reason":"tool_calls"}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""");

        Assert.Equal("stop", assembler.AssistantMessage!.Value.GetProperty("finish_reason").GetString());
    }

    [Fact]
    public void Append_CapturesUsageFromTheChunkThatCarriesIt()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[{"index":0,"delta":{"content":"hi"},"finish_reason":null}]}""");
        assembler.Append("""{"choices":[],"usage":{"prompt_tokens":7,"completion_tokens":3,"total_tokens":10}}""");

        var usage = assembler.Usage!.Value;
        Assert.Equal(7, usage.GetProperty("prompt_tokens").GetInt32());
        Assert.Equal(3, usage.GetProperty("completion_tokens").GetInt32());
        Assert.Equal(10, usage.GetProperty("total_tokens").GetInt32());
    }

    [Fact]
    public void Append_IgnoresDoneSentinelBlankAndMalformedPayloads()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("[DONE]");
        assembler.Append("   ");
        assembler.Append("");
        assembler.Append("{not json");
        assembler.Append("[1,2,3]");
        assembler.Append("\"a string\"");
        assembler.Append("""{"choices":"not-an-array"}""");
        assembler.Append("""{"choices":[{}]}""");
        assembler.Append("""{"choices":[{"index":0,"delta":"nope"}]}""");

        Assert.False(assembler.HasContent);
        Assert.Null(assembler.AssistantMessage);
        Assert.Null(assembler.Usage);
    }

    [Fact]
    public void Append_ToolCallOnlyStreamHasContentWithoutContentField()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"c1","function":{"name":"go","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""");

        var message = assembler.AssistantMessage!.Value;
        Assert.True(assembler.HasContent);
        Assert.False(message.TryGetProperty("content", out _));
        Assert.Equal("tool_calls", message.GetProperty("finish_reason").GetString());
    }

    [Fact]
    public void Append_ReasoningOnlyStreamProducesAssistantMessage()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[{"index":0,"delta":{"reasoning":"hmm"}}]}""");

        Assert.True(assembler.HasContent);
        Assert.Equal("hmm", assembler.AssistantMessage!.Value.GetProperty("reasoning").GetString());
    }

    [Fact]
    public void Append_UsageIsIndependentOfContentAssembly()
    {
        var assembler = new SseAssistantAssembler();

        assembler.Append("""{"choices":[],"usage":{"total_tokens":1}}""");

        Assert.False(assembler.HasContent);
        Assert.Equal(1, assembler.Usage!.Value.GetProperty("total_tokens").GetInt32());
    }
}
