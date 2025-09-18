using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
/// Describes a module and its dependencies for initialization ordering.
/// </summary>
/// <param name="Module">The module instance to be initialized.</param>
/// <param name="Dependencies">Collection of module types that this module depends on.</param>
public readonly record struct ModuleDescriptor(
    IModule Module,
    IReadOnlySet<Type> Dependencies)
{
    /// <summary>
    /// Gets the type of the module.
    /// </summary>
    public Type ModuleType => Module.GetType();

    /// <summary>
    /// Creates a module descriptor by analyzing the module's IRequires interfaces.
    /// </summary>
    /// <param name="module">The module to create a descriptor for.</param>
    /// <returns>A new ModuleDescriptor with analyzed dependencies.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when module is null.</exception>
    public static ModuleDescriptor Create(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var moduleType = module.GetType();
        
        // Find all IRequires<T> interfaces implemented by this module
        var dependencies = moduleType.GetInterfaces()
            .Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequires<>))
            .Select(static i => i.GetGenericArguments()[0]) // Extract T from IRequires<T>
            .ToHashSet();

        return new ModuleDescriptor(module, dependencies);
    }

    /// <summary>
    /// Determines whether this module has any dependencies.
    /// </summary>
    public bool HasDependencies => Dependencies.Count > 0;

    /// <summary>
    /// Determines whether this module depends on the specified module type.
    /// </summary>
    /// <param name="moduleType">The module type to check for dependency.</param>
    /// <returns>True if this module depends on the specified type; otherwise, false.</returns>
    public bool DependsOn(Type moduleType) => Dependencies.Contains(moduleType);
}