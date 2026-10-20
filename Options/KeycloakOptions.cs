namespace MetadataTagger.Options;

/// <summary>
/// Configuration for validating Keycloak access tokens. Used when
/// <see cref="AuthenticationOptions.Method"/> is
/// <see cref="AuthenticationMethods.Keycloak"/>. Requires <see cref="Authority"/>
/// to be set.
/// </summary>
public class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Maps Keycloak client roles (found in the token's <c>resource_access[ClientId].roles</c>
    /// claim) to application roles. Values are expected to match
    /// <see cref="MetadataTagging.Models.UserRoles"/> exactly.
    /// </summary>
    public List<KeycloakRoleMapping> RoleMappings { get; set; } = [];

    public string AuthorizationUrl => $"{Authority.TrimEnd('/')}/protocol/openid-connect/auth";
    public string TokenUrl => $"{Authority.TrimEnd('/')}/protocol/openid-connect/token";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Authority);
}

public class KeycloakRoleMapping
{
    public string KeycloakRole { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
