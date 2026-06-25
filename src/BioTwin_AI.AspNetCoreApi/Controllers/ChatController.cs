using System.Security.Claims;
using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Application.Chat;
using BioTwin_AI.DotNetShared.Auth;
using BioTwin_AI.DotNetShared.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController(IChatService chatService) : ControllerBase
{
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<ChatResponse>> Ask(ChatRequest request, CancellationToken cancellationToken)
    {
        return Ok(await chatService.AskAsync(GetTenantId(), IsInterviewer(), request, cancellationToken));
    }

    [HttpPost("stream")]
    [Authorize]
    public async Task Stream(ChatRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "application/x-ndjson";
        await foreach (var chunk in chatService.StreamAsync(GetTenantId(), IsInterviewer(), request, cancellationToken))
        {
            await Response.WriteAsync(JsonSerializer.Serialize(chunk), cancellationToken);
            await Response.WriteAsync("\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
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
