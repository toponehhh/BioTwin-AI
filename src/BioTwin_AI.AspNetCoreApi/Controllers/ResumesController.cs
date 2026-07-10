using System.Security.Claims;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[Route("api/resumes")]
public sealed class ResumesController(
    IResumeService resumeService,
    IResumeWizardExtractionService wizardExtractionService,
    IResumeConversionJobService conversionJobService,
    IResumeStateTokenService stateTokenService,
    IResumeImportJobService importJobService,
    IResumeOperationService operationService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ResumeSummaryDto>>> GetResumes(CancellationToken cancellationToken)
    {
        return Ok(await resumeService.GetSummariesAsync(GetTenantId(), cancellationToken));
    }

    [HttpGet("state")]
    [Authorize]
    public async Task<ActionResult<ResumeStateDto>> GetResumeState(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var summaries = await resumeService.GetSummariesAsync(tenantId, cancellationToken);
        var token = await stateTokenService.ComputeAsync(tenantId, cancellationToken);
        return Ok(new ResumeStateDto(token, summaries));
    }

    [HttpGet("{resumeId:int}")]
    [Authorize]
    public async Task<ActionResult<ResumeDetailDto>> GetResume(int resumeId, CancellationToken cancellationToken)
    {
        var resume = await resumeService.GetDetailAsync(GetTenantId(), resumeId, cancellationToken);
        return resume is null ? NotFound() : Ok(resume);
    }

    [HttpPost("upload/convert")]
    [Authorize]
    public async Task<ActionResult<ConvertedResumeFileDto>> ConvertUpload(IFormFile file, CancellationToken cancellationToken)
    {
        return Ok(await resumeService.ConvertUploadAsync(GetTenantId(), file, cancellationToken));
    }

    [HttpPost("upload/jobs")]
    [Authorize]
    public async Task<ActionResult<ResumeConversionJobDto>> StartConversionJob(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        return Ok(await conversionJobService.StartAsync(GetTenantId(), file, cancellationToken));
    }

    [HttpGet("upload/jobs/{jobId}")]
    [Authorize]
    public async Task<ActionResult<ResumeConversionJobDto>> GetConversionJob(
        string jobId,
        CancellationToken cancellationToken)
    {
        var job = await conversionJobService.GetAsync(GetTenantId(), jobId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost("import-jobs/wizard")]
    [Authorize]
    public async Task<ActionResult<ResumeImportJobDto>> StartWizardImport(
        [FromForm] IFormFile file,
        [FromForm] string operationId,
        [FromForm] string expectedStateToken,
        [FromForm] string language,
        [FromForm] string? title,
        CancellationToken cancellationToken)
    {
        return Ok(await importJobService.StartWizardAsync(
            GetRequiredUserId(),
            GetTenantId(),
            operationId,
            expectedStateToken,
            file,
            language,
            title,
            cancellationToken));
    }

    [HttpPost("import-jobs/workspace")]
    [Authorize]
    public async Task<ActionResult<ResumeImportJobDto>> StartWorkspaceImport(
        [FromForm] List<IFormFile> files,
        [FromForm] string operationId,
        [FromForm] string expectedStateToken,
        CancellationToken cancellationToken)
    {
        return Ok(await importJobService.StartWorkspaceAsync(
            GetRequiredUserId(),
            GetTenantId(),
            operationId,
            expectedStateToken,
            files,
            cancellationToken));
    }

    [HttpGet("import-jobs/{jobId}")]
    [Authorize]
    public async Task<ActionResult<ResumeImportJobDto>> GetImportJob(string jobId, CancellationToken cancellationToken)
    {
        var job = await importJobService.GetAsync(GetTenantId(), jobId, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpDelete("import-jobs/{jobId}")]
    [Authorize]
    public async Task<IActionResult> CancelImportJob(
        string jobId,
        [FromQuery] string operationId,
        CancellationToken cancellationToken)
    {
        if (!await importJobService.CancelAsync(GetTenantId(), jobId, cancellationToken))
        {
            return NotFound();
        }

        await operationService.ReleaseAsync(GetRequiredUserId(), operationId, cancellationToken);
        return NoContent();
    }

    [HttpPost("wizard/extract")]
    [Authorize]
    public async Task<ActionResult<ExtractResumeWizardResponse>> ExtractWizard(
        ExtractResumeWizardRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await wizardExtractionService.ExtractAsync(request, cancellationToken));
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ResumeDetailDto>> SaveResume(SaveResumeMarkdownRequest request, CancellationToken cancellationToken)
    {
        var resume = await resumeService.SaveAsync(GetTenantId(), request, GetUserId(), cancellationToken);
        return CreatedAtAction(nameof(GetResume), new { resumeId = resume.Id }, resume);
    }

    [HttpPut("{resumeId:int}/markdown")]
    [Authorize]
    public async Task<ActionResult<ResumeDetailDto>> ReplaceResumeMarkdown(int resumeId, SaveResumeMarkdownRequest request, CancellationToken cancellationToken)
    {
        var resume = await resumeService.ReplaceMarkdownAsync(GetTenantId(), resumeId, request, GetUserId(), cancellationToken);
        return resume is null ? NotFound() : Ok(resume);
    }

    [HttpPost("merge-preview")]
    [Authorize]
    public async Task<ActionResult<MergeResumeMarkdownResponse>> MergePreview(MergeResumeMarkdownRequest request, CancellationToken cancellationToken)
    {
        var preview = await resumeService.MergePreviewAsync(GetTenantId(), request, cancellationToken);
        return preview is null ? NotFound() : Ok(preview);
    }

    [HttpDelete("{resumeId:int}")]
    [Authorize]
    public async Task<IActionResult> DeleteResume(
        int resumeId,
        [FromQuery] string? operationId,
        [FromQuery] string? expectedStateToken,
        CancellationToken cancellationToken)
    {
        return await resumeService.DeleteAsync(
            GetTenantId(),
            resumeId,
            GetUserId(),
            operationId,
            expectedStateToken,
            cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpPost("rebuild-embeddings")]
    [Authorize]
    public async Task<ActionResult<RebuildEmbeddingsResponse>> RebuildEmbeddings(CancellationToken cancellationToken)
    {
        return Ok(await resumeService.RebuildEmbeddingsAsync(GetTenantId(), cancellationToken));
    }

    [HttpGet("{resumeId:int}/original")]
    [Authorize]
    public async Task<IActionResult> DownloadOriginal(int resumeId, CancellationToken cancellationToken)
    {
        var file = await resumeService.GetOriginalAsync(GetTenantId(), resumeId, cancellationToken);
        return file is null ? NotFound() : File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }

    private string GetTenantId()
    {
        return User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    }

    private int? GetUserId()
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
    }

    private int GetRequiredUserId()
    {
        return GetUserId() ?? throw new UnauthorizedAccessException("The authenticated session does not contain a user identifier.");
    }
}
