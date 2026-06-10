using System;
using System.Collections.Generic;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Defines a registry for managing service dependencies used throughout LiteBus.
///     This registry provides an abstraction over dependency injection containers,
///     allowing LiteBus to work with different DI frameworks.
/// </summary>
public interface IDependencyRegistry : IReadOnlyCollection<DependencyDescriptor>
{
    /// <summary>
    ///     Registers a service dependency in the registry.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor that defines how the service should be registered.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="descriptor" /> is <see langword="null" />.</exception>
    void Register(DependencyDescriptor descriptor);

    /// <summary>
    ///     Registers a service implementation as one entry in a multi-registration collection resolved through
    ///     <c>IEnumerable&lt;T&gt;</c> (or equivalent container collection resolution).
    /// </summary>
    /// <param name="descriptor">The dependency descriptor for a single collection item.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="descriptor" /> is <see langword="null" />.</exception>
    void RegisterCollection(DependencyDescriptor descriptor);
}