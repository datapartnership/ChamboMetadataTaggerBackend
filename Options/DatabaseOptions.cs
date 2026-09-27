namespace MetadataTagger.Options;

public class DatabaseOptions
{
    public const string Section = "Database";
    public bool UseManagedIdentity { get; set; } = false;
    public string ManagedIdentityClientId { get; set; } = string.Empty;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>
    /// When true, every table is dropped on startup and the schema is rebuilt from the
    /// migrations. Destroys all data on every restart while enabled.
    /// </summary>
    public bool ResetOnStartup { get; set; } = false;
}
