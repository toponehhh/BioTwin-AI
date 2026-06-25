using BioTwin_AI.AspNetCoreApi.Application.Refinement;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[Route("api/resumes/refine")]
public sealed class ResumeRefinementController(IResumeRefinementService refinementService) : ControllerBase
{
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<RefineMarkdownResponse>> Refine(RefineMarkdownRequest request, CancellationToken cancellationToken)
    {
        return Ok(await refinementService.RefineAsync(request, cancellationToken));
    }
}
