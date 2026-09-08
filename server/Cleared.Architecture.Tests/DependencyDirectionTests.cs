using System.Reflection;
using System.Xml.Linq;

namespace Cleared.Architecture.Tests;

public class DependencyDirectionTests
{
    [Fact]
    public void Domain_has_no_package_references()
    {
        var packageRefs = ReferencesOf(SolutionPaths.Domain, "PackageReference");

        Assert.True(packageRefs.Count == 0,
            "Cleared.Domain must stay framework-agnostic with zero PackageReference entries. " +
            $"Found: {string.Join(", ", packageRefs)}");
    }

    [Fact]
    public void Domain_does_not_reference_application()
    {
        AssertNoProjectReference(SolutionPaths.Domain, "Cleared.Application");
    }

    [Fact]
    public void Domain_does_not_reference_infrastructure()
    {
        AssertNoProjectReference(SolutionPaths.Domain, "Cleared.Infrastructure");
    }

    [Fact]
    public void Domain_does_not_reference_api()
    {
        AssertNoProjectReference(SolutionPaths.Domain, "Cleared.API");
    }

    [Fact]
    public void Application_does_not_reference_infrastructure()
    {
        AssertNoProjectReference(SolutionPaths.Application, "Cleared.Infrastructure");
    }

    [Fact]
    public void Application_does_not_reference_api()
    {
        AssertNoProjectReference(SolutionPaths.Application, "Cleared.API");
    }

    [Fact]
    public void Infrastructure_does_not_reference_api()
    {
        AssertNoProjectReference(SolutionPaths.Infrastructure, "Cleared.API");
    }

    [Fact]
    public void Controllers_do_not_return_domain_or_infrastructure_types()
    {
        var apiAssembly = typeof(Cleared.API.Controllers.InvoicesController).Assembly;
        var domainAssembly = typeof(Cleared.Domain.Common.Entity).Assembly;
        var infrastructureAssembly = typeof(Cleared.Infrastructure.Persistence.ClearedDbContext).Assembly;

        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => t is { IsAbstract: false, Name: var name } && name.EndsWith("Controller", StringComparison.Ordinal));

        var violations =
            from controller in controllerTypes
            from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            let payload = UnwrapPayloadType(action.ReturnType)
            where payload.Assembly == domainAssembly || payload.Assembly == infrastructureAssembly
            select $"{controller.Name}.{action.Name} returns {payload.FullName}";

        var violationList = violations.ToList();

        Assert.True(violationList.Count == 0,
            "Controllers must return Application-layer DTOs, never Domain or Infrastructure types " +
            "(that's how an entity's internals leak into the public API contract):\n" +
            string.Join('\n', violationList));
    }

    private static void AssertNoProjectReference(string projectPath, string forbiddenProjectName)
    {
        var references = ReferencesOf(projectPath, "ProjectReference");
        var violation = references.FirstOrDefault(r => r.Contains(forbiddenProjectName, StringComparison.OrdinalIgnoreCase));

        Assert.True(violation is null,
            $"{Path.GetFileNameWithoutExtension(projectPath)} must not depend on {forbiddenProjectName}, " +
            $"but its csproj includes a reference to: {violation}");
    }

    private static List<string> ReferencesOf(string projectPath, string elementName)
    {
        return XDocument.Load(projectPath)
            .Descendants(elementName)
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => include.Length > 0)
            .ToList();
    }

    // Peels Task<T>, ActionResult<T>, IReadOnlyList<T>, etc. down to the type actually
    // handed back to the caller, so a leaked entity can't hide inside a wrapper.
    private static Type UnwrapPayloadType(Type returnType)
    {
        var current = returnType;

        while (current.IsGenericType && current.GetGenericArguments().Length == 1)
        {
            current = current.GetGenericArguments()[0];
        }

        return current;
    }
}
