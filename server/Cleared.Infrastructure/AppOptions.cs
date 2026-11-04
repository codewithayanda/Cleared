namespace Cleared.Infrastructure;

public sealed class AppOptions
{
    public const string SectionName = "App";

    // Where people open the web app, for the links in emails. No path, for example http://localhost:4200.
    public string PublicUrl { get; set; } = string.Empty;
}
