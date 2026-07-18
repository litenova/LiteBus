using System.Xml.Linq;

namespace LiteBus.Runtime.UnitTests;

/// <summary>
///     Enforces the repository role dependency policy against every shipping project file.
/// </summary>
public sealed class ArchitectureDependencyPolicyTests
{
    /// <summary>
    ///     Confirms project and package references follow the allowed role dependency matrix.
    /// </summary>
    [Fact]
    public void SourceProjects_ShouldFollowRoleDependencyPolicy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectFiles = Directory.GetFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories);
        var projects = projectFiles.ToDictionary(
            path => Path.GetFileNameWithoutExtension(path)!,
            StringComparer.Ordinal);
        var projectReferences = projectFiles.ToDictionary(
            path => Path.GetFileNameWithoutExtension(path)!,
            path => ReadIncludes(XDocument.Load(path), "ProjectReference")
                .Select(GetProjectName)
                .ToArray(),
            StringComparer.Ordinal);
        var projectPackages = projectFiles.ToDictionary(
            path => Path.GetFileNameWithoutExtension(path)!,
            path => ReadIncludes(XDocument.Load(path), "PackageReference").ToArray(),
            StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var projectFile in projectFiles.OrderBy(path => path, StringComparer.Ordinal))
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile)!;

            if (!TryGetRole(projectName, out var sourceRole))
            {
                violations.Add($"{projectName}: no architecture role is assigned.");
                continue;
            }

            var document = XDocument.Load(projectFile);

            foreach (var reference in ReadIncludes(document, "ProjectReference"))
            {
                var referencedProject = GetProjectName(reference);

                if (!projects.ContainsKey(referencedProject))
                {
                    violations.Add($"{projectName}: project reference '{referencedProject}' does not resolve under src.");
                    continue;
                }

                if (!TryGetRole(referencedProject, out var targetRole))
                {
                    violations.Add($"{projectName}: referenced project '{referencedProject}' has no architecture role.");
                    continue;
                }

                if (!AllowedProjectRoles[sourceRole].Contains(targetRole))
                {
                    violations.Add(
                        $"{projectName} ({sourceRole}) may not reference {referencedProject} ({targetRole}).");
                }
                else if (sourceRole == ProjectRole.FeatureBridge &&
                         !IsFeatureBridgeReferenceAllowed(projectName, referencedProject))
                {
                    violations.Add(
                        $"{projectName} ({sourceRole}) may not cross-reference unrelated feature project " +
                        $"{referencedProject}.");
                }
            }

            var packages = projectPackages[projectName];

            foreach (var package in packages)
            {
                if (!IsPackageAllowed(sourceRole, package))
                {
                    violations.Add($"{projectName} ({sourceRole}) may not reference package '{package}'.");
                }
            }

            if (sourceRole is ProjectRole.TechnologyAdapter or ProjectRole.FeatureBridge)
            {
                var technologyFamilies = GetTransitiveTechnologyFamilies(
                    projectName,
                    projectReferences,
                    projectPackages);

                if (technologyFamilies.Count > 1)
                {
                    violations.Add(
                        $"{projectName} ({sourceRole}) references multiple technology SDK families: " +
                        string.Join(", ", technologyFamilies));
                }
            }
        }

        violations.Should().BeEmpty(string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    ///     Gets the project roles that each source role may reference.
    /// </summary>
    private static IReadOnlyDictionary<ProjectRole, IReadOnlySet<ProjectRole>> AllowedProjectRoles { get; } =
        new Dictionary<ProjectRole, IReadOnlySet<ProjectRole>>
        {
            [ProjectRole.PlatformContracts] = Set(ProjectRole.PlatformContracts),
            [ProjectRole.MediationContracts] = Set(ProjectRole.PlatformContracts, ProjectRole.MediationContracts),
            [ProjectRole.DurableContracts] = Set(
                ProjectRole.PlatformContracts,
                ProjectRole.MediationContracts,
                ProjectRole.DurableContracts),
            [ProjectRole.CoreImplementation] = Set(
                ProjectRole.PlatformContracts,
                ProjectRole.MediationContracts,
                ProjectRole.DurableContracts,
                ProjectRole.CoreImplementation),
            [ProjectRole.TechnologyAdapter] = Set(
                ProjectRole.PlatformContracts,
                ProjectRole.MediationContracts,
                ProjectRole.DurableContracts,
                ProjectRole.CoreImplementation,
                ProjectRole.TechnologyAdapter),
            [ProjectRole.FeatureBridge] = Set(
                ProjectRole.PlatformContracts,
                ProjectRole.MediationContracts,
                ProjectRole.DurableContracts,
                ProjectRole.CoreImplementation,
                ProjectRole.TechnologyAdapter,
                ProjectRole.FeatureBridge),
            [ProjectRole.HostAdapter] = Set(
                ProjectRole.PlatformContracts,
                ProjectRole.MediationContracts,
                ProjectRole.DurableContracts,
                ProjectRole.CoreImplementation,
                ProjectRole.TechnologyAdapter,
                ProjectRole.FeatureBridge,
                ProjectRole.HostAdapter),
            [ProjectRole.ConsumerTooling] = Set(
                ProjectRole.PlatformContracts,
                ProjectRole.MediationContracts,
                ProjectRole.DurableContracts,
                ProjectRole.CoreImplementation,
                ProjectRole.TechnologyAdapter,
                ProjectRole.FeatureBridge,
                ProjectRole.HostAdapter,
                ProjectRole.ConsumerTooling),
            [ProjectRole.Aggregate] = Set(
                ProjectRole.PlatformContracts,
                ProjectRole.MediationContracts,
                ProjectRole.DurableContracts,
                ProjectRole.CoreImplementation)
        };

    /// <summary>
    ///     Finds the repository root from the test output directory.
    /// </summary>
    /// <returns>The absolute repository root path.</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LiteBus.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the LiteBus repository root.");
    }

    /// <summary>
    ///     Reads non-empty Include attributes for one MSBuild item type.
    /// </summary>
    /// <param name="document">The project XML document.</param>
    /// <param name="itemName">The MSBuild item element name.</param>
    /// <returns>The included item values.</returns>
    private static IEnumerable<string> ReadIncludes(XDocument document, string itemName)
    {
        return document
            .Descendants(itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>();
    }

    /// <summary>
    ///     Gets a project name from an MSBuild reference on any operating system.
    /// </summary>
    /// <param name="reference">The project reference path.</param>
    /// <returns>The referenced project name.</returns>
    private static string GetProjectName(string reference)
    {
        return Path.GetFileNameWithoutExtension(reference.Replace('\\', '/'))!;
    }

    /// <summary>
    ///     Creates an ordinal set of allowed project roles.
    /// </summary>
    /// <param name="roles">The allowed roles.</param>
    /// <returns>The role set.</returns>
    private static IReadOnlySet<ProjectRole> Set(params ProjectRole[] roles)
    {
        return roles.ToHashSet();
    }

    /// <summary>
    ///     Determines whether a direct package reference is permitted for a project role.
    /// </summary>
    /// <param name="role">The project role.</param>
    /// <param name="package">The package identifier.</param>
    /// <returns><see langword="true" /> when the package is allowed; otherwise, <see langword="false" />.</returns>
    private static bool IsPackageAllowed(ProjectRole role, string package)
    {
        return role switch
        {
            ProjectRole.PlatformContracts or
                ProjectRole.MediationContracts or
                ProjectRole.DurableContracts or
                ProjectRole.Aggregate => false,
            ProjectRole.CoreImplementation => package == "Microsoft.Extensions.Logging.Abstractions",
            ProjectRole.TechnologyAdapter or ProjectRole.FeatureBridge =>
                IsTechnologyPackage(package) || package == "Microsoft.Extensions.Logging.Abstractions",
            ProjectRole.HostAdapter =>
                package.StartsWith("Autofac", StringComparison.Ordinal) ||
                package.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) ||
                package.StartsWith("OpenTelemetry", StringComparison.Ordinal),
            ProjectRole.ConsumerTooling =>
                package.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) ||
                package.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) ||
                package == "System.Collections.Immutable",
            _ => false
        };
    }

    /// <summary>
    ///     Determines whether a package is an approved persistence or transport SDK.
    /// </summary>
    /// <param name="package">The package identifier.</param>
    /// <returns><see langword="true" /> when the package is an approved technology SDK; otherwise, <see langword="false" />.</returns>
    private static bool IsTechnologyPackage(string package)
    {
        return GetTechnologyFamily(package) is not null;
    }

    /// <summary>
    ///     Determines whether a feature bridge references only its own axis or an explicitly bridged axis.
    /// </summary>
    /// <param name="projectName">The source feature bridge project.</param>
    /// <param name="referencedProject">The referenced project.</param>
    /// <returns><see langword="true" /> when the feature-axis reference is allowed.</returns>
    private static bool IsFeatureBridgeReferenceAllowed(string projectName, string referencedProject)
    {
        var sourceConcern = GetFeatureConcern(projectName);
        var targetConcern = GetFeatureConcern(referencedProject);

        if (sourceConcern is not null && targetConcern is not null && sourceConcern != targetConcern)
        {
            return false;
        }

        if (projectName.StartsWith("LiteBus.Inbox.", StringComparison.Ordinal))
        {
            return !referencedProject.StartsWith("LiteBus.Outbox", StringComparison.Ordinal) &&
                   !referencedProject.StartsWith("LiteBus.Saga", StringComparison.Ordinal);
        }

        if (projectName.StartsWith("LiteBus.Outbox.", StringComparison.Ordinal))
        {
            return !referencedProject.StartsWith("LiteBus.Inbox", StringComparison.Ordinal) &&
                   !referencedProject.StartsWith("LiteBus.Saga", StringComparison.Ordinal);
        }

        if (projectName.StartsWith("LiteBus.Saga.Storage.", StringComparison.Ordinal))
        {
            return !referencedProject.StartsWith("LiteBus.Inbox", StringComparison.Ordinal) &&
                   !referencedProject.StartsWith("LiteBus.Outbox", StringComparison.Ordinal);
        }

        if (projectName == "LiteBus.Saga.InboxIntegration")
        {
            return referencedProject.StartsWith("LiteBus.Saga", StringComparison.Ordinal) ||
                   referencedProject.StartsWith("LiteBus.Inbox", StringComparison.Ordinal) ||
                   referencedProject.StartsWith("LiteBus.Commands", StringComparison.Ordinal) ||
                   referencedProject.StartsWith("LiteBus.Messaging", StringComparison.Ordinal) ||
                   referencedProject.StartsWith("LiteBus.Runtime", StringComparison.Ordinal);
        }

        return true;
    }

    /// <summary>
    ///     Gets the durable feature concern encoded in a project name.
    /// </summary>
    /// <param name="projectName">The project name.</param>
    /// <returns>The concern name, or <see langword="null" /> for projects outside the adapter axes.</returns>
    private static string? GetFeatureConcern(string projectName)
    {
        if (projectName.Contains(".Storage.", StringComparison.Ordinal))
        {
            return "Storage";
        }

        if (projectName.Contains(".Dispatch.", StringComparison.Ordinal))
        {
            return "Dispatch";
        }

        if (projectName.Contains(".Ingress.", StringComparison.Ordinal))
        {
            return "Ingress";
        }

        return null;
    }

    /// <summary>
    ///     Collects technology SDK families from a project and every referenced project.
    /// </summary>
    /// <param name="projectName">The project whose dependency closure is inspected.</param>
    /// <param name="projectReferences">The project reference graph.</param>
    /// <param name="projectPackages">The direct package references by project.</param>
    /// <returns>The distinct technology SDK families in the transitive closure.</returns>
    private static IReadOnlySet<string> GetTransitiveTechnologyFamilies(
        string projectName,
        IReadOnlyDictionary<string, string[]> projectReferences,
        IReadOnlyDictionary<string, string[]> projectPackages)
    {
        var families = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(projectName);

        while (pending.TryPop(out var currentProject))
        {
            if (!visited.Add(currentProject))
            {
                continue;
            }

            foreach (var package in projectPackages[currentProject])
            {
                if (GetTechnologyFamily(package) is { } family)
                {
                    families.Add(family);
                }
            }

            foreach (var referencedProject in projectReferences[currentProject])
            {
                pending.Push(referencedProject);
            }
        }

        return families;
    }

    /// <summary>
    ///     Gets the persistence or broker SDK family represented by a package.
    /// </summary>
    /// <param name="package">The package identifier.</param>
    /// <returns>The SDK family, or <see langword="null" /> when the package is not a technology SDK.</returns>
    private static string? GetTechnologyFamily(string package)
    {
        if (package.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            return "EntityFrameworkCore";
        }

        return package switch
        {
            "AWSSDK.SQS" => "AwsSqs",
            "Azure.Messaging.ServiceBus" => "AzureServiceBus",
            "Confluent.Kafka" => "Kafka",
            "Npgsql" => "PostgreSql",
            "RabbitMQ.Client" => "Amqp",
            _ => null
        };
    }

    /// <summary>
    ///     Assigns a repository project to its architecture role.
    /// </summary>
    /// <param name="projectName">The project name.</param>
    /// <param name="role">The assigned role when recognized.</param>
    /// <returns><see langword="true" /> when the project is recognized; otherwise, <see langword="false" />.</returns>
    private static bool TryGetRole(string projectName, out ProjectRole role)
    {
        if (projectName is "LiteBus.Runtime.Abstractions" or "LiteBus.Transport.Abstractions")
        {
            role = ProjectRole.PlatformContracts;
            return true;
        }

        if (projectName is "LiteBus.Messaging.Abstractions" or
            "LiteBus.Commands.Abstractions" or
            "LiteBus.Queries.Abstractions" or
            "LiteBus.Events.Abstractions")
        {
            role = ProjectRole.MediationContracts;
            return true;
        }

        if (projectName is "LiteBus.DurableMessaging.Abstractions" or
            "LiteBus.Inbox.Abstractions" or
            "LiteBus.Outbox.Abstractions" or
            "LiteBus.Saga.Abstractions")
        {
            role = ProjectRole.DurableContracts;
            return true;
        }

        if (projectName is "LiteBus.Runtime" or
            "LiteBus.Messaging" or
            "LiteBus.Commands" or
            "LiteBus.Queries" or
            "LiteBus.Events" or
            "LiteBus.Inbox" or
            "LiteBus.Outbox" or
            "LiteBus.Saga" or
            "LiteBus.Transport")
        {
            role = ProjectRole.CoreImplementation;
            return true;
        }

        if (projectName is "LiteBus.Storage.EntityFrameworkCore" or "LiteBus.Storage.PostgreSql" ||
            projectName.StartsWith("LiteBus.Transport.", StringComparison.Ordinal) &&
            !projectName.Contains(".Extensions.", StringComparison.Ordinal))
        {
            role = ProjectRole.TechnologyAdapter;
            return true;
        }

        if (projectName.StartsWith("LiteBus.Inbox.Dispatch", StringComparison.Ordinal) ||
            projectName.StartsWith("LiteBus.Inbox.Ingress", StringComparison.Ordinal) ||
            projectName.StartsWith("LiteBus.Inbox.Storage", StringComparison.Ordinal) ||
            projectName.StartsWith("LiteBus.Outbox.Dispatch", StringComparison.Ordinal) ||
            projectName.StartsWith("LiteBus.Outbox.Storage", StringComparison.Ordinal) ||
            projectName.StartsWith("LiteBus.Saga.Storage", StringComparison.Ordinal) ||
            projectName == "LiteBus.Saga.InboxIntegration")
        {
            role = ProjectRole.FeatureBridge;
            return true;
        }

        if (projectName.Contains(".Extensions.", StringComparison.Ordinal) ||
            projectName.StartsWith("LiteBus.Extensions.", StringComparison.Ordinal))
        {
            role = ProjectRole.HostAdapter;
            return true;
        }

        if (projectName == "LiteBus.Analyzers" ||
            projectName.StartsWith("LiteBus.Testing", StringComparison.Ordinal))
        {
            role = ProjectRole.ConsumerTooling;
            return true;
        }

        if (projectName == "LiteBus")
        {
            role = ProjectRole.Aggregate;
            return true;
        }

        role = default;
        return false;
    }

    /// <summary>
    ///     Defines stable dependency roles for shipping projects.
    /// </summary>
    private enum ProjectRole
    {
        /// <summary>
        ///     Cross-cutting runtime or transport contracts.
        /// </summary>
        PlatformContracts,

        /// <summary>
        ///     Message mediation and semantic message contracts.
        /// </summary>
        MediationContracts,

        /// <summary>
        ///     Durable messaging and saga contracts.
        /// </summary>
        DurableContracts,

        /// <summary>
        ///     Default implementations that do not own external technology SDKs.
        /// </summary>
        CoreImplementation,

        /// <summary>
        ///     Broker or persistence technology implementations.
        /// </summary>
        TechnologyAdapter,

        /// <summary>
        ///     Bridges a feature axis to dispatch, ingress, storage, or another feature.
        /// </summary>
        FeatureBridge,

        /// <summary>
        ///     Host, dependency injection, diagnostics, or telemetry integration.
        /// </summary>
        HostAdapter,

        /// <summary>
        ///     Analyzer and consumer test support packages.
        /// </summary>
        ConsumerTooling,

        /// <summary>
        ///     The aggregate LiteBus package.
        /// </summary>
        Aggregate
    }
}
