namespace MetadataTagging.Models;

public enum FileCategory
{
    Text,
    Audio,
    Image,
    Video,
    Document,
    Other
}

public static class FileCategoryHelper
{
    private static readonly HashSet<string> AudioContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mpeg",
        "audio/mp3",
        "audio/wav",
        "audio/x-wav",
        "audio/ogg",
        "audio/flac",
        "audio/aac",
        "audio/mp4",
        "audio/webm",
        "audio/x-m4a",
        "audio/x-aiff",
        "audio/aiff",
        "audio/x-ms-wma"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma", ".aiff", ".webm", ".opus"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".csv", ".json", ".xml", ".yaml", ".yml", ".log", ".ini", ".cfg", ".html", ".htm", ".css", ".js", ".ts"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".webp", ".tiff", ".ico"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mov", ".wmv", ".mkv", ".webm", ".flv"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".rtf"
    };

    public static FileCategory FromContentType(string? contentType, string? fileName = null)
    {
        // Try content type first
        if (!string.IsNullOrEmpty(contentType))
        {
            if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                return FileCategory.Audio;
            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return FileCategory.Video;
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return FileCategory.Image;
            if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                return FileCategory.Text;
            if (contentType == "application/pdf" ||
                contentType.Contains("document", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("presentation", StringComparison.OrdinalIgnoreCase))
                return FileCategory.Document;
        }

        // Fall back to extension
        if (!string.IsNullOrEmpty(fileName))
        {
            var ext = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(ext))
            {
                if (AudioExtensions.Contains(ext)) return FileCategory.Audio;
                if (TextExtensions.Contains(ext)) return FileCategory.Text;
                if (ImageExtensions.Contains(ext)) return FileCategory.Image;
                if (VideoExtensions.Contains(ext)) return FileCategory.Video;
                if (DocumentExtensions.Contains(ext)) return FileCategory.Document;
            }
        }

        return FileCategory.Other;
    }

    public static bool IsAudio(string? contentType, string? fileName = null)
    {
        return FromContentType(contentType, fileName) == FileCategory.Audio;
    }

    public static bool IsAudioContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;
        return contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
               AudioContentTypes.Contains(contentType);
    }

    public static bool IsAudioExtension(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && AudioExtensions.Contains(ext);
    }

    public static string[] SupportedAudioExtensions => AudioExtensions.ToArray();
    public static string[] SupportedAudioContentTypes => AudioContentTypes.ToArray();
}
