namespace Cleared.Architecture.Tests;

internal static class SolutionPaths
{
    public static string ServerRoot { get; } = FindServerRoot();

    public static string Domain => Path.Combine(ServerRoot, "Cleared.Domain", "Cleared.Domain.csproj");

    public static string Application => Path.Combine(ServerRoot, "Cleared.Application", "Cleared.Application.csproj");

    public static string Infrastructure =>
        Path.Combine(ServerRoot, "Cleared.Infrastructure", "Cleared.Infrastructure.csproj");

    public static string Api => Path.Combine(ServerRoot, "Cleared.API", "Cleared.API.csproj");

    // Walk up from the test assembly's bin output until we find the solution file, so this
    // works from a dev machine and a CI runner without a hardcoded path.
    private static string FindServerRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cleared.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                $"Could not locate Cleared.slnx above {AppContext.BaseDirectory}");
        }

        return directory.FullName;
    }
}
