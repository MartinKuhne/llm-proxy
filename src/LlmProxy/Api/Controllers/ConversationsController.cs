namespace LlmProxy.Api.Controllers;

using System.Globalization;
using LlmProxy.Api.Responses;
using LlmProxy.Application.Services;
using LlmProxy.Domain;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Read-only operator API over the conversation audit log.
/// </summary>
[ApiController]
[Route("admin/api/conversations")]
public class ConversationsController : ControllerBase
{
    private readonly IConversationReader _reader;

    public ConversationsController(IConversationReader reader)
    {
        _reader = reader;
    }

    /// <summary>
    /// Lists conversations, most recent activity first. Bad paging and unparseable dates are normalised, not rejected.
    /// </summary>
    [HttpGet]
    public async Task<ConversationListDto> List(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? provider,
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        var query = new ConversationQuery(
            from: ParseDate(from),
            to: ParseDate(to),
            page: page ?? 1,
            pageSize: pageSize ?? ConversationQuery.DefaultPageSize,
            provider: provider,
            search: q);

        var result = await _reader.ListAsync(query, cancellationToken);

        return new ConversationListDto
        {
            Items = [.. result.Items.Select(ToListItem)],
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            TotalPages = result.TotalPages,
        };
    }

    /// <summary>
    /// Returns one conversation with the full replay of its events, or 404 when it is unknown.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var detail = await _reader.GetAsync(id, cancellationToken);

        return detail is null
            ? NotFound(new ErrorResponseDto
            {
                Error = new ErrorDetailDto
                {
                    Message = $"Conversation '{id}' was not found",
                    Type = "not_found",
                    Param = "id",
                    Code = "404",
                },
            })
            : Ok(ToDetailDto(detail));
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static ConversationListItemDto ToListItem(ConversationSummary summary) =>
        new()
        {
            Id = summary.Id,
            Title = summary.Title,
            StartedAt = summary.StartedAt,
            LastActivityAt = summary.LastActivityAt,
            TurnCount = summary.TurnCount,
            EventCount = summary.EventCount,
            Provider = summary.Provider,
            Model = summary.Model,
            TotalTokens = summary.TotalTokens,
            HasErrors = summary.HasErrors,
        };

    private static ConversationDetailDto ToDetailDto(ConversationDetail detail) =>
        new()
        {
            Id = detail.Id,
            Title = detail.Title,
            StartedAt = detail.StartedAt,
            LastActivityAt = detail.LastActivityAt,
            TurnCount = detail.TurnCount,
            EventCount = detail.EventCount,
            Provider = detail.Provider,
            Model = detail.Model,
            TotalTokens = detail.TotalTokens,
            HasErrors = detail.HasErrors,
            Events = [.. detail.Events.Select(ToEventDto)],
        };

    private static ConversationEventDto ToEventDto(ConversationEvent evt) =>
        new()
        {
            Kind = evt.Kind,
            Timestamp = evt.Timestamp,
            RequestId = evt.RequestId,
            Provider = evt.Provider,
            Model = evt.Model,
            Path = evt.Path,
            Data = evt.Data,
        };
}
