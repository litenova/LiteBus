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
    ///     Registers host-neutral background work that the container adapter maps to a generic-host background service when
    ///     supported.
    /// </summary>
    /// <param name="implementationType">The concrete type that implements <see cref="ILiteBusBackgroundWork" />.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="implementationType" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="implementationType" /> does not implement <see cref="ILiteBusBackgroundWork" />.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown when the adapter does not support background-work registration.</exception>
    void RegisterBackgroundWork(Type implementationType);
}