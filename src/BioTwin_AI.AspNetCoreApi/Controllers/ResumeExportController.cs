using System.Security.Claims;
using BioTwin_AI.AspNetCoreApi.Application.Export;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[Route("api/resumes/{resumeId:int}/export")]
public sealed class ResumeExportController(IResumeService resumeService, IResumePdfService pdfService) : ControllerBase
{
    [HttpGet("markdown")]
    [Authorize]
    public async Task<ActionResult<ResumeMarkdownExportDto>> ExportMarkdown(int resumeId, CancellationToken cancellationToken)
    {
        var markdown = await resumeService.ExportMarkdownAsync(GetTenantId(), resumeId, cancellationToken);
        return markdown is null ? NotFound() : Ok(markdown);
    }

    [HttpGet("pdf")]
    [Authorize]
    public async Task<IActionResult> ExportPdf(int resumeId, CancellationToken cancellationToken)
    {
        var markdown = await resumeService.ExportMarkdownAsync(GetTenantId(), resumeId, cancellationToken);
        if (markdown is null)
        {
            return NotFound();
        }

        var pdf = pdfService.Generate(markdown.Markdown, Path.GetFileNameWithoutExtension(markdown.FileName));
        return File(pdf, "application/pdf", Path.ChangeExtension(markdown.FileName, ".pdf"));
    }

    private string GetTenantId()
    {
        return User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    }
}
