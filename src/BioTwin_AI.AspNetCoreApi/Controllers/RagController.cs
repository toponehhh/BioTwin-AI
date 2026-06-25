using System.Security.Claims;
using BioTwin_AI.AspNetCoreApi.Application.Rag;
using BioTwin_AI.DotNetShared.Auth;
using BioTwin_AI.DotNetShared.Rag;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[Route("api/rag")]
public sealed class RagController(IRagSearchService ragSearchService) : ControllerBase
{
    [HttpPost("search")]
    [Authorize]
    public async Task<ActionResult<RagSearchResponse>> Search(RagSearchRequest request, CancellationToken cancellationToken)
    {
        return Ok(await ragSearchService.SearchAsync(GetTenantId(), IsInterviewer(), request, cancellationToken));
    }

    private string GetTenantId()
    {
        return User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    }

    private bool IsInterviewer()
    {
        return string.Equals(User.FindFirstValue(ClaimTypes.Role), UserRole.Interviewer.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
