using System.Security.Claims;
using MetadataTagging.Authentication;

namespace MetadataTagging.Tests;

public class KeycloakTokenClaimsNormalizerTests
{
    private const string ClientId = "chambo-api";

    [Fact]
    public void ResolveKeycloakClientRoles_FlattensRolesForConfiguredClient()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(
                "resource_access",
                """{"chambo-api":{"roles":["chambo-admin","chambo-tagger"]},"other-client":{"roles":["ignored"]}}""")
        ]));

        var roles = KeycloakTokenClaimsNormalizer.ResolveKeycloakClientRoles(principal, ClientId);

        Assert.Equal(2, roles.Count);
        Assert.Contains("chambo-admin", roles);
        Assert.Contains("chambo-tagger", roles);
    }

    [Fact]
    public void ResolveKeycloakClientRoles_ReturnsEmptyWhenClaimMissing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var roles = KeycloakTokenClaimsNormalizer.ResolveKeycloakClientRoles(principal, ClientId);

        Assert.Empty(roles);
    }

    [Fact]
    public void ResolveKeycloakClientRoles_ReturnsEmptyWhenClientIdMissingFromClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("resource_access", """{"other-client":{"roles":["chambo-admin"]}}""")
        ]));

        var roles = KeycloakTokenClaimsNormalizer.ResolveKeycloakClientRoles(principal, ClientId);

        Assert.Empty(roles);
    }

    [Fact]
    public void ResolveKeycloakClientRoles_ReturnsEmptyForMalformedJson()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("resource_access", "not valid json")
        ]));

        var roles = KeycloakTokenClaimsNormalizer.ResolveKeycloakClientRoles(principal, ClientId);

        Assert.Empty(roles);
    }
}
