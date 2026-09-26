namespace MetadataTagger.Options;

/// <summary>
/// Configuration for validating Azure Entra ID (Azure AD) access tokens.
/// Used when <see cref="AuthenticationOptions.Method"/> is
/// <see cref="AuthenticationMethods.EntraId"/>. Requires <see cref="TenantId"/>
/// and <see cref="ClientId"/> to be set.
/// </summary>
public class EntraIdOptions
{
    public const string Section = "EntraId";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Claim type that carries the Entra App Roles assigned to the signed-in user.
    /// Values are expected to match <see cref="MetadataTagging.Models.UserRoles"/> exactly.
    /// </summary>
    public string RoleClaimType { get; set; } = "roles";

    /// <summary>
    /// Claim type that carries Entra group object IDs.
    /// </summary>
    public string GroupClaimType { get; set; } = "groups";

    /// <summary>
    /// Maps Entra group object IDs to application roles when app roles are not
    /// emitted directly in the access token.
    /// </summary>
    public List<EntraGroupRoleMapping> GroupRoleMappings { get; set; } = [];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);
}

public class EntraGroupRoleMapping
{
    public string GroupId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
