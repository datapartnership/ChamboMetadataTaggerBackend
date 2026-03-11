namespace MetadataTagger.Options;

public class JwtOptions
{
    public const string Section = "JwtSettings";
    public required string SecretKey { get; set; }
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public int ExpiryMinutes { get; set; } = 480;
}
