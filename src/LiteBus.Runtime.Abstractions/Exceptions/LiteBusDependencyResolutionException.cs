using System;

namespace LiteBus.Runtime.Abstractions.Exceptions;

/// <summary>
///     Thrown when a required LiteBus service cannot be resolved from the dependency injection container.
/// </summary>
public sealed class LiteBusDependencyResolutionException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusDependencyResolutionException" /> class.
    /// </summary>
    /// <param name="serviceType">The unresolved service type.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceType" /> is <see langword="null" />.</exception>
    public LiteBusDependencyResolutionException(Type serviceType)
        : base($"Service of type '{serviceType.FullName ?? serviceType.Name}' is not registered.")
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        ServiceType = serviceType;
    }

    /// <summary>
    ///     Gets the unresolved service type.
    /// </summary>
    public Type ServiceType { get; }
}