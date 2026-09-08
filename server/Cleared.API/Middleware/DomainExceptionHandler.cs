using Cleared.Domain.Invoicing;
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
            // A well-formed request that's blocked by a legal/business rule about the
            // data itself — not malformed input (400) or a state conflict (409).
            TaxInvoiceValidationException => (StatusCodes.Status422UnprocessableEntity, "This tax invoice is missing required fields."),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request."),
            InvalidOperationException => (StatusCodes.Status409Conflict, "The request conflicts with the current state."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        };

        httpContext.Response.StatusCode = statusCode;

        var showDetail = environment.IsDevelopment() || statusCode != StatusCodes.Status500InternalServerError;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = showDetail ? exception.Message : null,
        };

        // Field-addressable, not just a sentence, so the Angular form can point at each
        // missing field individually rather than showing one opaque error string.
        if (exception is TaxInvoiceValidationException taxInvoiceValidationException)
        {
            problemDetails.Extensions["missingFields"] = taxInvoiceValidationException.MissingFields;
        }

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
