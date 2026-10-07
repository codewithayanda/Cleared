using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Cleared.API.RateLimiting;

public static partial class RateLimitingExtensions
{
    private static readonly TimeSpan MinimumRefill = TimeSpan.FromSeconds(1);

    public static IServiceCollection AddClearedRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .Validate(
                options => options.AllRules().All(rule => rule.PermitLimit > 0 && rule.Window > TimeSpan.Zero),
                "Every RateLimits rule needs a PermitLimit and a Window above zero.")
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitOptions>>((limiter, rules) => Configure(limiter, rules.Value));

        return services;
    }

    private static void Configure(RateLimiterOptions limiter, RateLimitOptions rules)
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.OnRejected = RejectAsync;

        // Everything not covered by a stricter policy: signed-in people count one by one,
        // everyone else by address. Probes from the load balancer are never counted.
        limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            context.Request.Path.StartsWithSegments("/health")
                ? RateLimitPartition.GetNoLimiter("health")
                : RateLimitPartition.GetTokenBucketLimiter(CallerKey(context), _ => Bucket(rules.Api)));

        limiter.AddPolicy(RateLimitPolicies.Login, context => ByAddress(context, rules.Login));
        limiter.AddPolicy(RateLimitPolicies.Register, context => ByAddress(context, rules.Register));
        limiter.AddPolicy(RateLimitPolicies.Session, context => ByAddress(context, rules.Session));
    }

    private static string CallerKey(HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return userId is null ? $"address:{ClientKey.From(context.Connection.RemoteIpAddress)}" : $"user:{userId}";
    }

    private static RateLimitPartition<string> ByAddress(HttpContext context, RateLimitRule rule) =>
        RateLimitPartition.GetTokenBucketLimiter(
            ClientKey.From(context.Connection.RemoteIpAddress), _ => Bucket(rule));

    // A bucket holds PermitLimit tokens and every request takes one. Tokens drip back at the rate
    // that adds up to PermitLimit a Window, in steps of at least a second so the timers stay few.
    private static TokenBucketRateLimiterOptions Bucket(RateLimitRule rule)
    {
        var perToken = rule.Window / rule.PermitLimit;
        var period = perToken < MinimumRefill ? MinimumRefill : perToken;

        return new TokenBucketRateLimiterOptions
        {
            TokenLimit = rule.PermitLimit,
            TokensPerPeriod = Math.Max(1, (int)Math.Round(rule.PermitLimit * period / rule.Window)),
            ReplenishmentPeriod = period,
            QueueLimit = 0,
        };
    }

    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "global";
        LogRejected(http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Cleared.API.RateLimiting"), policy, http.Request.Path);

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await http.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests. Wait a moment and try again.",
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Rate limit '{Policy}' refused a request to {Path}.")]
    private static partial void LogRejected(ILogger logger, string policy, PathString path);
}
