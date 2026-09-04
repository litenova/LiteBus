using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Runtime.Modules;

/// <summary>
///     Default implementation of <see cref="IModuleRegistry" /> that stores module descriptors
///     and resolves them in dependency order through <see cref="BuildOrder" />.
/// </summary>
internal sealed class ModuleRegistry : IModuleRegistry
{
    /// <summary>
    ///     The modules registered before dependency ordering is computed, in registration order.
    /// </summary>
    private readonly List<IModule> _orderedModules = [];

    /// <summary>
    ///     Module types already registered; duplicate registrations throw at configuration time.
    /// </summary>
    private readonly HashSet<Type> _registeredTypes = [];

    /// <summary>
    ///     Additional ordering edges inferred from composite parent-child relationships.
    /// </summary>
    private readonly Dictionary<Type, HashSet<Type>> _implicitDependenciesByModuleType = [];

    /// <summary>
    ///     Cached module descriptors sorted in dependency order.
    /// </summary>
    private IReadOnlyList<ModuleDescriptor>? _cachedOrderedModules;

    /// <summary>
    ///     Indicates whether <see cref="BuildOrder" /> has frozen further registration.
    /// </summary>
    private bool _isFrozen;

    /// <inheritdoc />
    public IModuleRegistry Register(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (_isFrozen)
        {
            throw new ModuleCompositionException(
                "Cannot register modules after BuildOrder() has been called. " +
                "Complete all module registration before building the module graph.");
        }

        List<IModule> stagedModules = [];
        HashSet<Type> stagedTypes = [];
        Dictionary<Type, HashSet<Type>> stagedDependencies = [];
        StageModule(module, stagedModules, stagedTypes, stagedDependencies);

        _orderedModules.AddRange(stagedModules);
        _registeredTypes.UnionWith(stagedTypes);

        foreach (var (moduleType, dependencies) in stagedDependencies)
        {
            _implicitDependenciesByModuleType[moduleType] = dependencies;
        }

        _cachedOrderedModules = null;

        return this;
    }

    /// <summary>
    ///     Expands one module and its composite descendants without mutating the live registry.
    /// </summary>
    /// <param name="module">The module currently being staged.</param>
    /// <param name="stagedModules">The modules staged by the current registration call.</param>
    /// <param name="stagedTypes">The module types staged by the current registration call.</param>
    /// <param name="stagedDependencies">Composite ordering edges staged by the current registration call.</param>
    /// <exception cref="ModuleCompositionException">Thrown when a module type is already registered or staged.</exception>
    private void StageModule(
        IModule module,
        List<IModule> stagedModules,
        HashSet<Type> stagedTypes,
        Dictionary<Type, HashSet<Type>> stagedDependencies)
    {
        ArgumentNullException.ThrowIfNull(module);

        var moduleType = module.GetType();

        if (_registeredTypes.Contains(moduleType) || !stagedTypes.Add(moduleType))
        {
            throw new ModuleCompositionException(
                $"Module '{moduleType.FullName ?? moduleType.Name}' is already registered. " +
                "Remove the duplicate registration or consolidate configuration into a single module instance. " +
                "Type-based module identity permits one module instance of each concrete type.");
        }

        stagedModules.Add(module);

        if (module is ICompositeModule composite)
        {
            if (!Enum.IsDefined(composite.BuildOrder))
            {
                throw new ModuleCompositionException(
                    $"Composite module '{moduleType.FullName ?? moduleType.Name}' returned an invalid build order.");
            }

            composite.DeclareChildren(child =>
            {
                ArgumentNullException.ThrowIfNull(child);

                var childType = child.GetType();
                StageModule(child, stagedModules, stagedTypes, stagedDependencies);

                var dependentType = composite.BuildOrder == CompositeModuleBuildOrder.ParentFirst
                    ? childType
                    : moduleType;
                var dependencyType = composite.BuildOrder == CompositeModuleBuildOrder.ParentFirst
                    ? moduleType
                    : childType;

                if (!stagedDependencies.TryGetValue(dependentType, out var dependencies))
                {
                    dependencies = [];
                    stagedDependencies.Add(dependentType, dependencies);
                }

                dependencies.Add(dependencyType);
            });
        }
    }

    /// <inheritdoc />
    public bool IsModuleRegistered<T>() where T : IModule
    {
        return _registeredTypes.Contains(typeof(T));
    }

    /// <inheritdoc />
    public IReadOnlyList<ModuleDescriptor> BuildOrder()
    {
        if (_cachedOrderedModules is not null)
        {
            return _cachedOrderedModules;
        }

        if (_orderedModules.Count == 0)
        {
            _cachedOrderedModules = [];
            _isFrozen = true;
            return _cachedOrderedModules;
        }

        var descriptors = _orderedModules.Select(module =>
        {
            var descriptor = ModuleDescriptor.Create(module);

            if (!_implicitDependenciesByModuleType.TryGetValue(descriptor.ModuleType, out var implicitDependencies))
            {
                return descriptor;
            }

            var dependencies = descriptor.Dependencies
                .Concat(implicitDependencies)
                .ToFrozenSet();

            return new ModuleDescriptor(module, dependencies);
        }).ToList();
        _cachedOrderedModules = TopologicalSort(descriptors);
        _isFrozen = true;
        return _cachedOrderedModules;
    }

    /// <summary>
    ///     Performs topological sorting on module descriptors to determine initialization order.
    /// </summary>
    /// <param name="descriptors">The module descriptors to sort.</param>
    /// <returns>Module descriptors in dependency order (dependencies first).</returns>
    /// <exception cref="ModuleCompositionException">
    ///     Thrown when circular dependencies are detected or when a dependency is missing.
    /// </exception>
    private static ReadOnlyCollection<ModuleDescriptor> TopologicalSort(
        IReadOnlyList<ModuleDescriptor> descriptors)
    {
        var descriptorsByType = descriptors.ToDictionary(static d => d.ModuleType, static d => d);
        List<ModuleDescriptor> result = [];
        HashSet<Type> visited = [];
        HashSet<Type> visiting = [];
        List<Type> visitingPath = [];

        foreach (var descriptor in descriptors)
        {
            Visit(descriptor.ModuleType, descriptorsByType, visited, visiting, visitingPath, result);
        }

        return result.AsReadOnly();
    }

    /// <summary>
    ///     Recursively visits a module and its dependencies using depth-first search.
    /// </summary>
    /// <param name="moduleType">The current module type being visited.</param>
    /// <param name="descriptorsByType">Dictionary mapping module types to their descriptors.</param>
    /// <param name="visited">Set of already processed module types.</param>
    /// <param name="visiting">Set of module types currently being processed (for cycle detection).</param>
    /// <param name="visitingPath">Ordered module path currently being processed for cycle diagnostics.</param>
    /// <param name="result">The result list where modules are added in dependency order.</param>
    /// <exception cref="ModuleCompositionException">
    ///     Thrown when a circular dependency is detected or when a required dependency is missing.
    /// </exception>
    private static void Visit(
        Type moduleType,
        IReadOnlyDictionary<Type, ModuleDescriptor> descriptorsByType,
        ISet<Type> visited,
        ISet<Type> visiting,
        IList<Type> visitingPath,
        IList<ModuleDescriptor> result)
    {
        if (visited.Contains(moduleType))
        {
            return;
        }

        if (!visiting.Add(moduleType))
        {
            var cycleStart = visitingPath.IndexOf(moduleType);
            var cycle = visitingPath
                .Skip(cycleStart < 0 ? 0 : cycleStart)
                .Append(moduleType)
                .Select(static type => type.Name);

            throw new ModuleCompositionException(
                $"Circular dependency detected: {string.Join(" -> ", cycle)}. " +
                "Check your IRequires<T> declarations for cycles.");
        }

        visitingPath.Add(moduleType);
        var descriptor = descriptorsByType[moduleType];

        foreach (var dependencyType in descriptor.Dependencies.OrderBy(
                     static type => type.FullName ?? type.Name,
                     StringComparer.Ordinal))
        {
            if (!descriptorsByType.ContainsKey(dependencyType))
            {
                throw new ModuleCompositionException(
                    $"Module '{moduleType.Name}' requires '{dependencyType.Name}', " +
                    "but it is not registered. Ensure all required modules are added to the module registry.");
            }

            Visit(dependencyType, descriptorsByType, visited, visiting, visitingPath, result);
        }

        visitingPath.RemoveAt(visitingPath.Count - 1);
        visiting.Remove(moduleType);
        visited.Add(moduleType);
        result.Add(descriptor);
    }
}
