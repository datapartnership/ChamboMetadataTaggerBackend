using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MetadataTagging.Data;
using MetadataTagging.Models;
using MetadataTagging.DTOs;

namespace MetadataTagging.Controllers;

[ApiController]
[Route("api/applications")]
public class ApplicationsController(ApplicationDbContext db) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(typeof(DatasetAccessApplicationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        CreateDatasetAccessApplicationRequest request,
        CancellationToken ct)
    {
        ValidateRequiredText(request);
        if (!request.OpenSourceCommitment)
        {
            ModelState.AddModelError(
                nameof(request.OpenSourceCommitment),
                "You must commit to open-source publication of derived insights and/or models.");
        }
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var application = new DatasetAccessApplication
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Organization = request.Organization.Trim(),
            OrganizationEmail = request.OrganizationEmail.Trim(),
            JobTitle = request.JobTitle.Trim(),
            ProjectDescription = request.ProjectDescription.Trim(),
            EthicalDataHandling = request.EthicalDataHandling.Trim(),
            OpenSourceCommitment = true,
            SubmittedAtUtc = DateTime.UtcNow
        };

        db.DatasetAccessApplications.Add(application);
        await db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new DatasetAccessApplicationResponse
        {
            Id = application.Id,
            SubmittedAtUtc = application.SubmittedAtUtc
        });
    }

    private void ValidateRequiredText(CreateDatasetAccessApplicationRequest request)
    {
        var fields = new Dictionary<string, string>
        {
            [nameof(request.FirstName)] = request.FirstName,
            [nameof(request.LastName)] = request.LastName,
            [nameof(request.Organization)] = request.Organization,
            [nameof(request.OrganizationEmail)] = request.OrganizationEmail,
            [nameof(request.JobTitle)] = request.JobTitle,
            [nameof(request.ProjectDescription)] = request.ProjectDescription,
            [nameof(request.EthicalDataHandling)] = request.EthicalDataHandling
        };

        foreach (var (name, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(value))
                ModelState.AddModelError(name, "This field is required.");
        }
    }

    [HttpGet]
    [Authorize(Policy = "Write")]
    [ProducesResponseType(typeof(CatalogPagedResult<DatasetAccessApplicationListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<CatalogPagedResult<DatasetAccessApplicationListItem>>> List(
        [FromQuery] DatasetAccessApplicationListQuery query,
        CancellationToken ct)
    {
        var applications = db.DatasetAccessApplications
            .AsNoTracking()
            .OrderByDescending(application => application.SubmittedAtUtc);

        var totalCount = await applications.CountAsync(ct);
        var results = await applications
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(application => new DatasetAccessApplicationListItem
            {
                Id = application.Id,
                FirstName = application.FirstName,
                LastName = application.LastName,
                Organization = application.Organization,
                OrganizationEmail = application.OrganizationEmail,
                JobTitle = application.JobTitle,
                ProjectDescription = application.ProjectDescription,
                EthicalDataHandling = application.EthicalDataHandling,
                OpenSourceCommitment = application.OpenSourceCommitment,
                SubmittedAtUtc = application.SubmittedAtUtc
            })
            .ToListAsync(ct);

        return Ok(new CatalogPagedResult<DatasetAccessApplicationListItem>
        {
            page = query.Page,
            pageSize = query.PageSize,
            totalCount = totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize),
            results = results
        });
    }
}
