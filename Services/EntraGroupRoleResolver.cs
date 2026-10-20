using System.Security.Claims;
using MetadataTagger.Options;

namespace MetadataTagging.Authentication;

public static class EntraGroupRoleResolver
{
    public static IReadOnlyCollection<string> ResolveRoles(
        ClaimsPrincipal principal,
        EntraIdOptions options)
    {
        var roles = principal.FindAll(options.RoleClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groupIds = principal.FindAll(options.GroupClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in options.GroupRoleMappings)
        {
            if (groupIds.Contains(mapping.GroupId))
            {
                roles.Add(mapping.Role);
            }
        }

        return roles;
    }
}
