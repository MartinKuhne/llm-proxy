namespace GreedyLlm.Application.Services;

using GreedyLlm.Api.Requests;
using GreedyLlm.Api.Responses;

public interface IChatCompletionService
{
    Task<ChatCompletionResponseDto> CompleteAsync(
        ChatCompletionRequestDto request,
        CancellationToken cancellationToken = default);
}
