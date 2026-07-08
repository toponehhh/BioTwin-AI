using BioTwin_AI.AspNetCoreApi.Application.Profiles;
using BioTwin_AI.DotNetShared.Profiles;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[Route("api/public/candidate-profile")]
public sealed class PublicCandidateProfileController(IPublicCandidateProfileService profileService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CandidateProfileDto>> Get([FromQuery] string? uid, CancellationToken cancellationToken)
    {
        var profile = await profileService.GetAsync(uid, cancellationToken);
        return profile is null ? NotFound() : Ok(profile);
    }
}
