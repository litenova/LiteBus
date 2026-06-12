using System;
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
            throw new LiteBusConfigurationException(
                "Cannot register modules after BuildOrder() has been called. " +
                "Complete all module registration before building the module graph.");
        }

        var moduleType = module.GetType();

        if (!_registeredTypes.Add(moduleType))
        {
            throw new LiteBusConfigurationException(
                $"Module '{moduleType.FullName ?? moduleType.Name}' is already registered. " +
                "Remove the duplicate registration or consolidate configuration into a single module instance. " +
                "For MessageModule, call AddMessageModule() once before semantic modules such as AddCommandModule().");
        }

        _orderedModules.Add(module);

        if (module is ICompositeModule composite)
        {
            composite.DeclareChildren(child => Register(child));
        }

        _cachedOrderedModules = null;

        return this;
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

        var descriptors = _orderedModules.Select(ModuleDescriptor.Create).ToList();
        _cachedOrderedModules = TopologicalSort(descriptors);
        _isFrozen = true;
        return _cachedOrderedModules;
    }

    /// <summary>
    ///     Performs topological sorting on module descriptors to determine initialization order.
    /// </summary>
    /// <param name="descriptors">The module descriptors to sort.</param>
    /// <returns>Module descriptors in dependency order (dependencies first).</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when circular dependencies are detected or when a dependency is missing.
    /// </exception>
    private static ReadOnlyCollection<ModuleDescriptor> TopologicalSort(
        IReadOnlyList<ModuleDescriptor> descriptors)
    {
        var descriptorsByType = descriptors.ToDictionary(static d => d.ModuleType, static d => d);
        List<ModuleDescriptor> result = [];
        HashSet<Type> visited = [];
        HashSet<Type> visiting = [];

        foreach (var descriptor in descriptors)
        {
            Visit(descriptor.ModuleType, descriptorsByType, visited, visiting, result);
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
    /// <param name="result">The result list where modules are added in dependency order.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when a circular dependency is detected or when a required dependency is missing.
    /// </exception>
    private static void Visit(
        Type moduleType,
        IReadOnlyDictionary<Type, ModuleDescriptor> descriptorsByType,
        ISet<Type> visited,
        ISet<Type> visiting,
        IList<ModuleDescriptor> result)
    {
        if (visited.Contains(moduleType))
        {
            return;
        }

        if (!visiting.Add(moduleType))
        {
            throw new LiteBusConfigurationException(
                $"Circular dependency detected involving module '{moduleType.Name}'. " +
                "Check your IRequires<T> declarations for cycles.");
        }

        var descriptor = descriptorsByType[moduleType];

        foreach (var dependencyType in descriptor.Dependencies)
        {
            if (!descriptorsByType.ContainsKey(dependencyType))
            {
                throw new LiteBusConfigurationException(
                    $"Module '{moduleType.Name}' requires '{dependencyType.Name}', " +
                    "but it is not registered. Ensure all required modules are added to the module registry.");
            }

            Visit(dependencyType, descriptorsByType, visited, visiting, result);
        }

        visiting.Remove(moduleType);
        visited.Add(moduleType);
        result.Add(descriptor);
    }
}
