using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Cleared.API.Security;

// Browsers label every request with Sec-Fetch-Site, and a page on another site has no business
// calling the sign-in endpoints. It runs before the body is read, and SameSite=Strict on the
// refresh cookie is the second barrier. Clients that send no label, such as curl, pass through.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RejectCrossSiteAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.HttpContext.Request.Headers["Sec-Fetch-Site"] == "cross-site")
        {
            context.Result = new ObjectResult(
                new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Cross-site requests are not allowed here." })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
