using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.Modules;

/// <summary>
///     Default implementation of <see cref="IModuleRegistry" /> that stores module descriptors
///     and provides them in dependency-resolved order through enumeration.
/// </summary>
internal sealed class ModuleRegistry : IModuleRegistry
{
    private readonly HashSet<IModule> _modules = [];
    private IReadOnlyList<ModuleDescriptor>? _cachedOrderedModules;

    /// <inheritdoc />
    public IModuleRegistry Register(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _modules.Add(module);

        // Invalidate cache when new modules are added
        _cachedOrderedModules = null;

        return this;
    }

    /// <inheritdoc />
    public bool IsModuleRegistered<T>() where T : IModule
    {
        return _modules.Any(module => module.GetType() == typeof(T));
    }

    /// <summary>
    ///     Gets the ordered module descriptors, using caching for performance.
    /// </summary>
    /// <returns>Module descriptors in dependency order.</returns>
    /// <exception cref="System.InvalidOperationException">
    ///     Thrown when circular dependencies are detected or when required dependencies are missing.
    /// </exception>
    private IReadOnlyList<ModuleDescriptor> GetOrderedModules()
    {
        if (_cachedOrderedModules is not null)
            return _cachedOrderedModules;

        if (_modules.Count == 0)
        {
            _cachedOrderedModules = [];
            return _cachedOrderedModules;
        }

        // Create descriptors for all registered modules
        var descriptors = _modules.Select(ModuleDescriptor.Create).ToList();

        // Perform topological sort to determine dependency order
        _cachedOrderedModules = TopologicalSort(descriptors);
        return _cachedOrderedModules;
    }

    /// <summary>
    ///     Performs topological sorting on module descriptors to determine initialization order.
    /// </summary>
    /// <param name="descriptors">The module descriptors to sort.</param>
    /// <returns>Module descriptors in dependency order (dependencies first).</returns>
    /// <exception cref="System.InvalidOperationException">
    ///     Thrown when circular dependencies are detected or when a dependency is missing.
    /// </exception>
    private static IReadOnlyList<ModuleDescriptor> TopologicalSort(
        IReadOnlyList<ModuleDescriptor> descriptors)
    {
        var descriptorsByType = descriptors.ToDictionary(static d => d.ModuleType, static d => d);
        var result = new List<ModuleDescriptor>();
        var visited = new HashSet<Type>();
        var visiting = new HashSet<Type>(); // For cycle detection

        // Visit each module in the dependency graph
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
    /// <exception cref="System.InvalidOperationException">
    ///     Thrown when a circular dependency is detected or when a required dependency is missing.
    /// </exception>
    private static void Visit(
        Type moduleType,
        IReadOnlyDictionary<Type, ModuleDescriptor> descriptorsByType,
        ISet<Type> visited,
        ISet<Type> visiting,
        IList<ModuleDescriptor> result)
    {
        // Skip if already processed
        if (visited.Contains(moduleType)) return;

        // Detect circular dependencies
        if (!visiting.Add(moduleType))
        {
            throw new InvalidOperationException(
                $"Circular dependency detected involving module '{moduleType.Name}'. " +
                "Check your IRequires<T> declarations for cycles.");
        }

        var descriptor = descriptorsByType[moduleType];

        // Process all dependencies first (depth-first)
        foreach (var dependencyType in descriptor.Dependencies)
        {
            // Ensure the dependency is registered
            if (!descriptorsByType.ContainsKey(dependencyType))
            {
                throw new InvalidOperationException(
                    $"Module '{moduleType.Name}' requires '{dependencyType.Name}', " +
                    "but it is not registered. Ensure all required modules are added to the module registry.");
            }

            // Recursively visit the dependency
            Visit(dependencyType, descriptorsByType, visited, visiting, result);
        }

        // Mark as fully processed and add to result
        visiting.Remove(moduleType);
        visited.Add(moduleType);
        result.Add(descriptor);
    }

    #region IOrderedEnumerable<ModuleDescriptor> Implementation

    /// <inheritdoc />
    public IEnumerator<ModuleDescriptor> GetEnumerator()
    {
        return GetOrderedModules().GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public IOrderedEnumerable<ModuleDescriptor> CreateOrderedEnumerable<TKey>(
        Func<ModuleDescriptor, TKey> keySelector,
        IComparer<TKey>? comparer,
        bool descending)
    {
        var orderedModules = GetOrderedModules();

        return descending
            ? orderedModules.OrderByDescending(keySelector, comparer)
            : orderedModules.OrderBy(keySelector, comparer);
    }

    #endregion
}