using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MetadataTagging.Models;
using MetadataTagger.Options;

namespace MetadataTagging.Services;

public class GroupRoleClaimsTransformation : IClaimsTransformation
{
    private const string RolesAlreadyMapped = "RolesAlreadyMapped";

    private readonly AzureAdGroupMappingOptions _groupMapping;
    private readonly ILogger<GroupRoleClaimsTransformation> _logger;

    public GroupRoleClaimsTransformation(IOptions<AzureAdGroupMappingOptions> options, ILogger<GroupRoleClaimsTransformation> logger)
    {
        _groupMapping = options.Value;
        _logger = logger;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Avoid re-adding claims on repeated calls within the same request
        if (principal.HasClaim(c => c.Type == RolesAlreadyMapped))
            return Task.FromResult(principal);

        // TODO: remove debug logging
        var allClaims = principal.Claims.Select(c => $"{c.Type}={c.Value}");
        _logger.LogWarning("DEBUG claims: {Claims}", string.Join(" | ", allClaims));

        var groupIds = principal.FindAll("groups").Select(c => c.Value).ToHashSet();

        _logger.LogWarning("DEBUG token groups: [{Groups}]", string.Join(", ", groupIds));
        _logger.LogWarning("DEBUG config AdminGroupId: [{Id}] match={Match}", _groupMapping.AdminGroupId, groupIds.Contains(_groupMapping.AdminGroupId ?? ""));
        _logger.LogWarning("DEBUG config TaggerGroupId: [{Id}] match={Match}", _groupMapping.TaggerGroupId, groupIds.Contains(_groupMapping.TaggerGroupId ?? ""));
        _logger.LogWarning("DEBUG config SupervisorGroupId: [{Id}] match={Match}", _groupMapping.SupervisorGroupId, groupIds.Contains(_groupMapping.SupervisorGroupId ?? ""));

        var rolesToAdd = new List<string>();

        if (!string.IsNullOrEmpty(_groupMapping.AdminGroupId) && groupIds.Contains(_groupMapping.AdminGroupId))
            rolesToAdd.Add(UserRoles.Admin);

        if (!string.IsNullOrEmpty(_groupMapping.SupervisorGroupId) && groupIds.Contains(_groupMapping.SupervisorGroupId))
            rolesToAdd.Add(UserRoles.Supervisor);

        if (!string.IsNullOrEmpty(_groupMapping.TaggerGroupId) && groupIds.Contains(_groupMapping.TaggerGroupId))
            rolesToAdd.Add(UserRoles.Tagger);

        var identity = new ClaimsIdentity(principal.Identity);
        identity.AddClaim(new Claim(RolesAlreadyMapped, "true"));

        foreach (var role in rolesToAdd)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

        _logger.LogWarning("DEBUG roles mapped: [{Roles}]", string.Join(", ", rolesToAdd));

        return Task.FromResult(new ClaimsPrincipal(identity));
    }
}
