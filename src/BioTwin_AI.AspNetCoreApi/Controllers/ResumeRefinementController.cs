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
    public ActionResult<RefineMarkdownResponse> Refine(RefineMarkdownRequest request)
    {
        return Ok(refinementService.Refine(request));
    }
}
