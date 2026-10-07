using Microsoft.AspNetCore.Mvc.Filters;

namespace Cleared.API.Security;

// Tokens must never sit in a browser or proxy cache.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NoStoreAttribute : ActionFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
    }
}
