using Microsoft.AspNetCore.Mvc;
using MetadataTagging.Models;

namespace MetadataTagging.Services;

/// <summary>Streams a storage file to the client, honouring a single-range <c>Range</c> header.</summary>
public static class StorageStreaming
{
    public static async Task<IActionResult> StreamStorageFileAsync(
        this ControllerBase controller, IFileStorageService storage, string key, CancellationToken ct = default)
    {
        var contentType = FileCategoryHelper.GetContentType(key);
        var response = controller.Response;
        response.Headers.AcceptRanges = "bytes";

        var rangeHeader = controller.Request.Headers.Range.ToString();
        if (string.IsNullOrEmpty(rangeHeader))
            return controller.File(await storage.OpenReadAsync(key, ct), contentType, enableRangeProcessing: false);

        var entry = await storage.GetEntryAsync(key, ct)
            ?? throw new FileNotFoundException($"File '{key}' not found in storage.", key);

        if (!TryParseRange(rangeHeader, entry.Size, out var start, out var end))
        {
            response.Headers.ContentRange = $"bytes */{entry.Size}";
            return controller.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
        }

        var length = end - start + 1;
        var stream = await storage.OpenReadRangeAsync(key, start, length, ct);
        response.StatusCode = StatusCodes.Status206PartialContent;
        response.Headers.ContentRange = $"bytes {start}-{end}/{entry.Size}";
        response.ContentLength = length;
        return new FileStreamResult(stream, contentType);
    }

    /// <summary>Parses the first range of <c>bytes=start-end</c>, <c>bytes=start-</c> or <c>bytes=-suffix</c>.</summary>
    internal static bool TryParseRange(string header, long size, out long start, out long end)
    {
        start = end = 0;
        if (size <= 0 || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = header["bytes=".Length..].Split(',')[0].Trim().Split('-');
        if (parts.Length != 2)
            return false;

        if (parts[0].Length == 0)
        {
            if (!long.TryParse(parts[1], out var suffix) || suffix <= 0)
                return false;
            start = Math.Max(0, size - suffix);
            end = size - 1;
            return true;
        }

        if (!long.TryParse(parts[0], out start) || start >= size)
            return false;

        if (parts[1].Length == 0)
        {
            end = size - 1;
            return true;
        }

        if (!long.TryParse(parts[1], out end) || end < start)
            return false;
        end = Math.Min(end, size - 1);
        return true;
    }
}
