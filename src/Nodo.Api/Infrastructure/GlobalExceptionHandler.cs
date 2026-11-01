using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Nodo.Api.Infrastructure;

/// <summary>
/// Maps exceptions to ProblemDetails (see <see cref="ProblemMapping"/>): 400 validation, 404 not found, 409 conflict,
/// 422 domain rule (with its stable <c>code</c>). An invariant never produces a 500.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var info = ProblemMapping.Map(exception);
        if (info is null)
        {
            logger.LogError(exception, "Unhandled exception");
            info = new ProblemInfo(StatusCodes.Status500InternalServerError, "Error interno", "Ocurrió un error inesperado.", null, null);
        }

        var problem = new ProblemDetails { Status = info.Status, Title = info.Title, Detail = info.Detail };
        if (info.Code is not null)
            problem.Extensions["code"] = info.Code;
        if (info.Errors is not null)
            problem.Extensions["errors"] = info.Errors;

        httpContext.Response.StatusCode = info.Status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
