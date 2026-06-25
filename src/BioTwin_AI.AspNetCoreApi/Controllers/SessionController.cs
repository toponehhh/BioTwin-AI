using BioTwin_AI.AspNetCoreApi.Application.Auth;
using BioTwin_AI.DotNetShared.Auth;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[Route("api/session")]
public sealed class SessionController(ISessionResponseFactory sessionResponseFactory) : ControllerBase
{
    [HttpGet("current")]
    public async Task<ActionResult<CurrentSessionResponse>> GetCurrent(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Ok(sessionResponseFactory.CreateAnonymous());
        }

        var username = User.Identity.Name ?? string.Empty;
        var roleText = User.FindFirstValue(ClaimTypes.Role);
        var role = Enum.TryParse<UserRole>(roleText, ignoreCase: true, out var parsedRole)
            ? parsedRole
            : UserRole.Candidate;
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _ = int.TryParse(userIdClaim, out var userId);

        return Ok(await sessionResponseFactory.CreateAuthenticatedAsync(userId, username, role, cancellationToken));
    }
}
