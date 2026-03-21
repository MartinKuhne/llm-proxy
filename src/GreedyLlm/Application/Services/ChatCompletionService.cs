namespace GreedyLlm.Application.Services;

using GreedyLlm.Api.Requests;
using GreedyLlm.Api.Responses;

public class ChatCompletionService : IChatCompletionService
{
    public Task<ChatCompletionResponseDto> CompleteAsync(
        ChatCompletionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
