using System.Security.Claims;
using MetadataTagging.Authentication;
using MetadataTagging.Models;

namespace MetadataTagging.Tests;

public class EntraTokenClaimsNormalizerTests
{
    [Fact]
    public void AddLocalApplicationIdentity_ReplacesEntraNameIdentifierWithLocalUserId()
    {
        var entraIdentity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "d7ce8b8d-b1dc-42f8-a280-7e68d80bb68d"),
            new Claim("oid", "d7ce8b8d-b1dc-42f8-a280-7e68d80bb68d")
        ],
        "EntraId");
        var principal = new ClaimsPrincipal(entraIdentity);

        EntraTokenClaimsNormalizer.AddLocalApplicationIdentity(
            principal,
            42,
            "Test User",
            "test.user@example.com",
            UserRoles.Tagger,
            "EntraId");

        Assert.Equal("42", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Single(principal.FindAll(ClaimTypes.NameIdentifier));
        Assert.Equal(
            "d7ce8b8d-b1dc-42f8-a280-7e68d80bb68d",
            principal.FindFirstValue("oid"));
        Assert.Equal(UserRoles.Tagger, principal.FindFirstValue(ClaimTypes.Role));
    }
}
