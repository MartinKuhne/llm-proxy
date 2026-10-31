namespace LlmProxy.Application.Services;

using LlmProxy.Domain;

/// <summary>
/// One page of conversation summaries plus the totals the paging controls need.
/// </summary>
/// <param name="Items">Summaries on this page, most recently active first.</param>
/// <param name="Page">1-based page number that was served.</param>
/// <param name="PageSize">Entries per page that was served.</param>
/// <param name="TotalCount">Number of conversations matching the filter, before paging.</param>
/// <param name="TotalPages">Pages available for <paramref name="PageSize"/>; 0 when nothing matched.</param>
public sealed record ConversationPage(
    IReadOnlyList<ConversationSummary> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
