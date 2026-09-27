using System.IO.Compression;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace MetadataTagging.Services;

/// <summary>A ZIP archive assembled on the fly by <see cref="WriteAsync"/>.</summary>
/// <param name="FileName">Download file name, e.g. <c>clip01.zip</c>.</param>
/// <param name="WriteAsync">Adds entries to the archive; responsible for disposing any streams it opened.</param>
public record ZipBundle(string FileName, Func<ZipArchive, CancellationToken, Task> WriteAsync);

/// <summary>
/// Streams a <see cref="ZipBundle"/> directly to the response body without buffering it
/// in memory or on disk.
/// </summary>
public sealed class ZipStreamResult(ZipBundle bundle) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;

        // ZipArchive only writes synchronously, which Kestrel rejects by default.
        var bodyControl = http.Features.Get<IHttpBodyControlFeature>();
        if (bodyControl != null)
            bodyControl.AllowSynchronousIO = true;

        http.Response.ContentType = "application/zip";
        http.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = bundle.FileName
        }.ToString();

        using var archive = new ZipArchive(http.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
        await bundle.WriteAsync(archive, http.RequestAborted);
    }
}
