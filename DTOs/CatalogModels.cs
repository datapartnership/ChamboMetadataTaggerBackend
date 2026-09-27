namespace MetadataTagging.DTOs;

/// <summary>Paginated wrapper for any catalog list.</summary>
public class CatalogPagedResult<T>
{
    public int page { get; set; }
    public int pageSize { get; set; }
    public int totalCount { get; set; }
    public int totalPages { get; set; }
    public List<T> results { get; set; } = [];
}

public class FacetItem
{
    public string value { get; set; } = "";
    public int count { get; set; }
}
