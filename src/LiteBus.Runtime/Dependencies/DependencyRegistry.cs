using System;
using System.Collections;
using System.Collections.Generic;

namespace LiteBus.Runtime.Dependencies;

/// <summary>
/// Default implementation of <see cref="IDependencyRegistry"/> that manages dependency descriptors
/// used throughout LiteBus. This registry provides an abstraction over dependency injection containers
/// and prevents duplicate registrations.
/// </summary>
public sealed class DependencyRegistry : IDependencyRegistry
{
    private readonly HashSet<DependencyDescriptor> _descriptors = [];

    /// <summary>
    /// Gets the total number of dependency descriptors registered in the registry.
    /// </summary>
    /// <value>The total count of registered dependency descriptors.</value>
    public int Count => _descriptors.Count;

    /// <summary>
    /// Registers a dependency in the registry if it hasn't been registered already.
    /// Duplicate registrations (same dependency type and implementation type) are ignored.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor that defines how the dependency should be registered.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
    public void Register(DependencyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        _descriptors.Add(descriptor);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the dependency descriptors.
    /// </summary>
    /// <returns>An enumerator that can be used to iterate through the dependency descriptors.</returns>
    public IEnumerator<DependencyDescriptor> GetEnumerator()
    {
        return _descriptors.GetEnumerator();
    }

    /// <summary>
    /// Returns an enumerator that iterates through the dependency descriptors.
    /// </summary>
    /// <returns>An enumerator that can be used to iterate through the dependency descriptors.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}