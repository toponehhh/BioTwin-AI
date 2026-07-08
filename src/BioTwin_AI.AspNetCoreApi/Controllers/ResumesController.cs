using System.Security.Claims;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[Route("api/resumes")]
public sealed class ResumesController(IResumeService resumeService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ResumeSummaryDto>>> GetResumes(CancellationToken cancellationToken)
    {
        return Ok(await resumeService.GetSummariesAsync(GetTenantId(), cancellationToken));
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
    public async Task<IActionResult> DeleteResume(int resumeId, CancellationToken cancellationToken)
    {
        return await resumeService.DeleteAsync(GetTenantId(), resumeId, cancellationToken) ? NoContent() : NotFound();
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
}
