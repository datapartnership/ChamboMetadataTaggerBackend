namespace MetadataTagging.Models;

public class DefaultUserSettings
{
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
}

public class DefaultAdminSettings : DefaultUserSettings
{
    public const string Section = "DefaultAdmin";
}

public class DefaultTaggerSettings : DefaultUserSettings
{
    public const string Section = "DefaultTagger";
}

public class DefaultSupervisorSettings : DefaultUserSettings
{
    public const string Section = "DefaultSupervisor";
}
