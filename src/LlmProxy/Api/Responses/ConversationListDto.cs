namespace LlmProxy.Api.Responses;

/// <summary>
/// A page of conversation entries plus the totals the paging UI needs.
/// </summary>
public class ConversationListDto
{
    public List<ConversationListItemDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}
