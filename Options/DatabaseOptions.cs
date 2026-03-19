namespace MetadataTagger.Options;

public class DatabaseOptions
{
    public const string Section = "Database";
    public string Provider { get; set; } = "sqlite";
    public bool UseManagedIdentity { get; set; } = false;
    public string ManagedIdentityClientId { get; set; } = string.Empty;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DataSource { get; set; } = "app.db";
}
