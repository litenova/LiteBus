using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LiteBus.Runtime.UnitTests;

/// <summary>
///     Enforces that every InternalsVisibleTo grant under src/ names a project in the solution and takes the
///     public key from the shared LiteBusPublicKey property.
/// </summary>
public sealed partial class InternalsVisibleToPolicyTests
{
    [Fact]
    public void Grants_ShouldNameSolutionProjectsAndUseTheSharedKeyProperty()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionProjects = XDocument.Load(Path.Combine(repositoryRoot, "LiteBus.slnx"))
            .Descendants("Project")
            .Select(project => Path.GetFileNameWithoutExtension((string) project.Attribute("Path")!))
            .ToHashSet(StringComparer.Ordinal);

        var files = Directory.GetFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Append(Path.Combine(repositoryRoot, "src", "Directory.Build.props"));

        var violations = new List<string>();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var relative = Path.GetRelativePath(repositoryRoot, file);

            if (LiteralKeyPattern().IsMatch(text) && !relative.EndsWith("Directory.Build.props", StringComparison.Ordinal))
            {
                violations.Add($"{relative}: pastes the public key literally; use PublicKey=$(LiteBusPublicKey).");
            }

            foreach (var grant in XDocument.Parse(text).Descendants("InternalsVisibleTo"))
            {
                var include = (string?) grant.Attribute("Include") ?? string.Empty;
                var assemblyName = include.Split(',')[0].Trim();

                if (!solutionProjects.Contains(assemblyName))
                {
                    violations.Add($"{relative}: grants internals to '{assemblyName}', which is not a project in LiteBus.slnx.");
                }
            }
        }

        violations.Should().BeEmpty();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LiteBus.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("LiteBus.slnx was not found.");
    }

    [GeneratedRegex("[0-9a-f]{320}")]
    private static partial Regex LiteralKeyPattern();
}
