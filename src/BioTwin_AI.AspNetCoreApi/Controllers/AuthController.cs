using System.Security.Claims;
using BioTwin_AI.AspNetCoreApi.Application.Auth;
using BioTwin_AI.DotNetShared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService, IExternalProviderCatalog providerCatalog) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResult>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        if (result.Success && result.Session is not null)
        {
            await SignInAsync(result.Session);
        }

        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResult>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        if (result.Success && result.Session is not null)
        {
            await SignInAsync(result.Session);
        }

        return result.Success ? Ok(result) : Unauthorized(result);
    }

    [HttpPost("interviewer-login")]
    public async Task<ActionResult<AuthResult>> InterviewerLogin(CancellationToken cancellationToken)
    {
        var result = await authService.InterviewerLoginAsync(cancellationToken);
        if (result.Session is not null)
        {
            await SignInAsync(result.Session);
        }

        return Ok(result);
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<ActionResult<AuthResult>> UpdateProfile(
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var result = await authService.UpdateProfileAsync(username, request, cancellationToken);
        if (result.Success && result.Session is not null)
        {
            await SignInAsync(result.Session);
        }

        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpGet("external/providers")]
    public ActionResult<IReadOnlyList<ExternalIdentityProviderDto>> GetExternalProviders()
    {
        return Ok(providerCatalog.GetProviders(new HashSet<string>()));
    }

    private Task SignInAsync(CurrentSessionResponse session)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId?.ToString() ?? string.Empty),
            new(ClaimTypes.Name, session.Username ?? string.Empty),
            new(ClaimTypes.Role, session.Role.ToString()),
            new("display_name", session.DisplayName ?? session.Username ?? string.Empty),
            new("avatar", session.Avatar ?? string.Empty)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}
