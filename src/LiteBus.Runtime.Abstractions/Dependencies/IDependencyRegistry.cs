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
    ///     Registers a hosted background service implementation with the underlying container when the adapter supports
    ///     hosted-service registration.
    /// </summary>
    /// <param name="implementationType">The concrete hosted-service type to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="implementationType" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="implementationType" /> is not assignable to a hosted service contract.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the adapter does not support hosted-service registration.</exception>
    void RegisterHostedService(Type implementationType);
}