using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Middleware;

// Maps the exceptions domain entities already throw (ArgumentException on bad input,
// InvalidOperationException on an illegal state transition) to clean ProblemDetails
// responses, instead of a raw 500 stack trace reaching the client.
public sealed class DomainExceptionHandler(IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request."),
            InvalidOperationException => (StatusCodes.Status409Conflict, "The request conflicts with the current state."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        };

        httpContext.Response.StatusCode = statusCode;

        var showDetail = environment.IsDevelopment() || statusCode != StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = showDetail ? exception.Message : null,
            },
            cancellationToken);

        return true;
    }
}
