namespace Bms.Application.Common;

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}

public class PageRequest
{
    private const int MaxPageSize = 200;
    private int _pageSize = 25;

    public int Page { get; init; } = 1;

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; }
}
