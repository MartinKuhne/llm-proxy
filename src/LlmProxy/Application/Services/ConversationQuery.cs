namespace LlmProxy.Application.Services;

/// <summary>
/// Filter and paging criteria for listing conversations. Out-of-range paging is normalised on construction so the
/// reader never has to reject a request.
/// </summary>
public sealed record ConversationQuery
{
    /// <summary>Page size used when the caller asks for none.</summary>
    internal const int DefaultPageSize = 50;

    /// <summary>Largest page size the reader serves.</summary>
    internal const int MaxPageSize = 200;

    public ConversationQuery(
        DateOnly? from = null,
        DateOnly? to = null,
        int page = 1,
        int pageSize = DefaultPageSize,
        string? provider = null,
        string? search = null)
    {
        From = from;
        To = to;
        Page = page < 1 ? 1 : page;
        PageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider;
        Search = string.IsNullOrWhiteSpace(search) ? null : search;
    }

    /// <summary>Inclusive start date, UTC, compared against the last activity date.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Inclusive end date, UTC, compared against the last activity date.</summary>
    public DateOnly? To { get; init; }

    /// <summary>1-based page number, never below 1.</summary>
    public int Page { get; init; }

    /// <summary>Entries per page, between 1 and <see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; init; }

    /// <summary>Exact, case-insensitive provider filter; null means no filter.</summary>
    public string? Provider { get; init; }

    /// <summary>Case-insensitive substring match on title, id, provider and model; null means no filter.</summary>
    public string? Search { get; init; }
}
