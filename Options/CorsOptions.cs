namespace MetadataTagger.Options;

public class CorsOptions
{
    public const string Section = "Cors";

    /// <summary>
    /// List of allowed origins. Set to ["*"] to allow any origin (default behaviour).
    /// </summary>
    public string[] AllowedOrigins { get; set; } = ["*"];
}
