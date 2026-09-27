using System.Security.Claims;
using MetadataTagger.Options;
using MetadataTagging.Authentication;
using MetadataTagging.Models;

namespace MetadataTagging.Tests;

public class EntraGroupRoleResolverTests
{
    [Fact]
    public void ResolveRoles_MapsAllMatchingGroupsAndPreservesNativeRoles()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("roles", UserRoles.Supervisor),
            new Claim("groups", "admin-group"),
            new Claim("groups", "tagger-group")
        ]));
        var options = new EntraIdOptions
        {
            GroupRoleMappings =
            [
                new EntraGroupRoleMapping { GroupId = "admin-group", Role = UserRoles.Admin },
                new EntraGroupRoleMapping { GroupId = "tagger-group", Role = UserRoles.Tagger },
                new EntraGroupRoleMapping { GroupId = "other-group", Role = UserRoles.Supervisor }
            ]
        };

        var roles = EntraGroupRoleResolver.ResolveRoles(principal, options);

        Assert.Equal(3, roles.Count);
        Assert.Contains(UserRoles.Admin, roles);
        Assert.Contains(UserRoles.Supervisor, roles);
        Assert.Contains(UserRoles.Tagger, roles);
    }

    [Fact]
    public void ResolveRoles_MatchesGroupObjectIdsCaseInsensitively()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("groups", "A2C4104E-1723-4FE0-92C0-5B7F7F3DEAE0")
        ]));
        var options = new EntraIdOptions
        {
            GroupRoleMappings =
            [
                new EntraGroupRoleMapping
                {
                    GroupId = "a2c4104e-1723-4fe0-92c0-5b7f7f3deae0",
                    Role = UserRoles.Admin
                }
            ]
        };

        var roles = EntraGroupRoleResolver.ResolveRoles(principal, options);

        Assert.Equal([UserRoles.Admin], roles);
    }
}
