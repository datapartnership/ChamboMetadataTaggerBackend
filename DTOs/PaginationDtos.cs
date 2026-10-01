namespace MetadataTagging.DTOs;

public class PaginationParams
{
    private int _page = 1;
    private int _pageSize = 20;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 1 : value > 100 ? 100 : value;
    }

    public string? SortBy { get; set; }
    public string SortOrder { get; set; } = "asc";

    public bool IsDescending => SortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase);
}

public class PagedResponse<T>
{
    public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

/// <summary>
/// One page of a folder's immediate children. <see cref="PagedResponse{T}.TotalCount"/> counts those
/// children (sub-folders + direct files); <see cref="TotalFileCount"/> counts every file beneath the folder.
/// </summary>
public class FolderPagedResponse<T> : PagedResponse<T>
{
    public string Folder { get; set; } = "";
    public int TotalFileCount { get; set; }
}
