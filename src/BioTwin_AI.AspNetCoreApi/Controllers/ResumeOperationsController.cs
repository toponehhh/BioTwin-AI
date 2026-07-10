using System.Security.Claims;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[Authorize]
[Route("api/resumes/operations")]
public sealed class ResumeOperationsController(IResumeOperationService operationService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ResumeOperationLeaseDto>> Acquire(
        AcquireResumeOperationRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => operationService.AcquireAsync(
            GetUserId(),
            GetTenantId(),
            request.OperationType,
            request.ExpectedStateToken,
            cancellationToken));
    }

    [HttpPut("{operationId}/heartbeat")]
    public async Task<ActionResult<ResumeOperationLeaseDto>> Heartbeat(
        string operationId,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(() => operationService.HeartbeatAsync(GetUserId(), operationId, cancellationToken));
    }

    [HttpDelete("{operationId}")]
    public async Task<IActionResult> Release(string operationId, CancellationToken cancellationToken)
    {
        try
        {
            await operationService.ReleaseAsync(GetUserId(), operationId, cancellationToken);
            return NoContent();
        }
        catch (ResumeOperationConflictException exception)
        {
            return StatusCode(exception.StatusCode, ToError(exception));
        }
    }

    private async Task<ActionResult<ResumeOperationLeaseDto>> ExecuteAsync(
        Func<Task<ResumeOperationLeaseDto>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (ResumeOperationConflictException exception)
        {
            return StatusCode(exception.StatusCode, ToError(exception));
        }
    }

    private static ResumeApiErrorDto ToError(ResumeOperationConflictException exception)
    {
        return new ResumeApiErrorDto(
            exception.Code,
            exception.Message,
            exception.RecoveryHint,
            exception.CanRetry);
    }

    private string GetTenantId() => User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";

    private int GetUserId()
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : throw new UnauthorizedAccessException("The authenticated session does not contain a user identifier.");
    }
}
