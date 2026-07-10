using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ResumeApiExceptionFilterTests
{
    [Fact]
    public void Filter_maps_resume_conflict_to_structured_409_response()
    {
        var context = CreateContext(new ResumeOperationConflictException(
            "resume_state_stale",
            "The resume changed.",
            "Refresh and retry."));

        new ResumeApiExceptionFilter().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        var error = Assert.IsType<ResumeApiErrorDto>(result.Value);
        Assert.Equal("resume_state_stale", error.Code);
        Assert.Equal("Refresh and retry.", error.RecoveryHint);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void Filter_maps_import_validation_to_structured_400_response()
    {
        var context = CreateContext(new ResumeImportException(
            "invalid_file_size",
            "The file is too large.",
            "Choose a smaller file.",
            false));

        new ResumeApiExceptionFilter().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("invalid_file_size", Assert.IsType<ResumeApiErrorDto>(result.Value).Code);
    }

    private static ExceptionContext CreateContext(Exception exception)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor());
        return new ExceptionContext(actionContext, []) { Exception = exception };
    }
}
