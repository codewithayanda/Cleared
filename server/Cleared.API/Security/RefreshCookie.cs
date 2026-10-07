namespace Cleared.API.Security;

// The only place that knows how the refresh token travels. It is an HttpOnly cookie, so a script
// on the page cannot read it, and its path keeps the browser from sending it anywhere but the
// sign-in endpoints. Secure is off only in Development, where the API is served over plain http.
public sealed class RefreshCookie(IHostEnvironment environment)
{
    public const string Name = "cleared_refresh";

    public const string CookiePath = "/api/v1/auth";

    public string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public void Write(HttpResponse response, string token, DateTimeOffset expires) =>
        response.Cookies.Append(Name, token, Options(expires));

    // Deleting needs the same path and flags the cookie was set with, or the browser keeps it.
    public void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = !environment.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Expires = expires,
        IsEssential = true,
    };
}
