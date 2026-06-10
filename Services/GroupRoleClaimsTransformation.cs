using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MetadataTagging.Models;
using MetadataTagger.Options;

namespace MetadataTagging.Services;

public class GroupRoleClaimsTransformation : IClaimsTransformation
{
    private const string AlreadyMapped = "RolesAlreadyMapped";

    private readonly AzureAdGroupMappingOptions _groupMapping;

    public GroupRoleClaimsTransformation(IOptions<AzureAdGroupMappingOptions> options)
    {
        _groupMapping = options.Value;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Avoid re-adding claims on repeated calls within the same request
        if (principal.HasClaim(c => c.Type == AlreadyMapped))
            return Task.FromResult(principal);

        var groupIds = principal.FindAll("groups").Select(c => c.Value).ToHashSet();

        var rolesToAdd = new List<string>();

        if (!string.IsNullOrEmpty(_groupMapping.AdminGroupId) && groupIds.Contains(_groupMapping.AdminGroupId))
            rolesToAdd.Add(UserRoles.Admin);

        if (!string.IsNullOrEmpty(_groupMapping.SupervisorGroupId) && groupIds.Contains(_groupMapping.SupervisorGroupId))
            rolesToAdd.Add(UserRoles.Supervisor);

        if (!string.IsNullOrEmpty(_groupMapping.TaggerGroupId) && groupIds.Contains(_groupMapping.TaggerGroupId))
            rolesToAdd.Add(UserRoles.Tagger);

        var identity = new ClaimsIdentity(principal.Identity);
        identity.AddClaim(new Claim(AlreadyMapped, "true"));

        foreach (var role in rolesToAdd)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

        return Task.FromResult(new ClaimsPrincipal(identity));
    }
}
