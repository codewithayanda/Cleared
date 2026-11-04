using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace Cleared.API.Security;

public static class TooManyRequestsResult
{
    // A 429 that says how long to wait, in the header the client already reads.
    public static ObjectResult Create(ControllerBase controller, TimeSpan wait, string title)
    {
        controller.Response.Headers.RetryAfter = Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.InvariantCulture);

        return controller.StatusCode(
            StatusCodes.Status429TooManyRequests,
            new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = title });
    }
}
