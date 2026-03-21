namespace GreedyLlm.Api.Controllers;

using GreedyLlm.Api.Requests;
using GreedyLlm.Api.Responses;
using GreedyLlm.Application.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("v1/chat")]
public class ChatController : ControllerBase
{
    private readonly IChatCompletionService _chatCompletionService;

    public ChatController(IChatCompletionService chatCompletionService)
    {
        _chatCompletionService = chatCompletionService;
    }

    [HttpPost("completions")]
    public async Task<ActionResult<ChatCompletionResponseDto>> CompleteAsync(
        [FromBody] ChatCompletionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _chatCompletionService.CompleteAsync(request, cancellationToken);
        return Ok(response);
    }
}
