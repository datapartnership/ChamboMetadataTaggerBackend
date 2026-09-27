namespace MetadataTagger.Options;

/// <summary>
/// Supported values for <see cref="AuthenticationOptions.Method"/>.
/// </summary>
public static class AuthenticationMethods
{
    public const string Jwt = "Jwt";
    public const string EntraId = "EntraId";
    public const string Keycloak = "Keycloak";
}

/// <summary>
/// Selects which authentication scheme the API accepts. Nested
/// <c>EntraId</c> / <c>Keycloak</c> / <c>JwtSettings</c> sections may still
/// contain values for other environments; only the method named here is
/// registered at startup.
/// </summary>
public class AuthenticationOptions
{
    public const string Section = "Authentication";

    /// <summary>
    /// Active authentication method. One of: <c>Jwt</c>, <c>EntraId</c>, <c>Keycloak</c>.
    /// Defaults to <c>Jwt</c> (local email/password tokens).
    /// </summary>
    public string Method { get; set; } = AuthenticationMethods.Jwt;

    public bool IsJwt =>
        Method.Equals(AuthenticationMethods.Jwt, StringComparison.OrdinalIgnoreCase);

    public bool IsEntraId =>
        Method.Equals(AuthenticationMethods.EntraId, StringComparison.OrdinalIgnoreCase);

    public bool IsKeycloak =>
        Method.Equals(AuthenticationMethods.Keycloak, StringComparison.OrdinalIgnoreCase);

    public bool IsKnownMethod => IsJwt || IsEntraId || IsKeycloak;
}
