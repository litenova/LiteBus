using System;
using System.Collections;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.Extensions.Microsoft.DependencyInjection;

/// <summary>
///     Adapter that bridges LiteBus dependency registration with Microsoft DI container,
///     handling duplicate registrations gracefully using descriptor equality.
/// </summary>
internal sealed class MicrosoftDependencyRegistryAdapter : IDependencyRegistry
{
    /// <summary>
    ///     Tracks descriptors already translated into Microsoft DI service registrations.
    /// </summary>
    private readonly HashSet<DependencyDescriptor> _registeredDescriptors = [];

    /// <summary>
    ///     Tracks background-work implementation types already registered with the service collection.
    /// </summary>
    private readonly HashSet<Type> _registeredBackgroundWork = [];

    /// <summary>
    ///     The service collection receiving LiteBus dependency registrations.
    /// </summary>
    private readonly IServiceCollection _services;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MicrosoftDependencyRegistryAdapter" /> class.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    public MicrosoftDependencyRegistryAdapter(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    ///     Gets the number of unique dependency descriptors that have been registered.
    /// </summary>
    public int Count => _registeredDescriptors.Count;

    /// <summary>
    ///     Registers a dependency descriptor with the underlying service collection if not already registered.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Duplicate descriptors are silently ignored based on the descriptor's equality implementation.
    ///     This prevents duplicate service registrations when multiple modules attempt to register the same services.
    /// </remarks>
    public void Register(DependencyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // Use HashSet.Add, which uses IEquatable<DependencyDescriptor>
        // Returns false if the descriptor is already present.
        if (!_registeredDescriptors.Add(descriptor))
        {
            // Descriptor already registered, skip silently.
            return;
        }

        var serviceDescriptor = ConvertToServiceDescriptor(descriptor);
        _services.Add(serviceDescriptor);
    }

    /// <summary>
    ///     Returns an enumerator that iterates through the registered dependency descriptors.
    /// </summary>
    /// <returns>An enumerator for the registered dependency descriptors.</returns>
    public IEnumerator<DependencyDescriptor> GetEnumerator()
    {
        return _registeredDescriptors.GetEnumerator();
    }

    /// <summary>
    ///     Returns a non-generic enumerator that iterates through the registered dependency descriptors.
    /// </summary>
    /// <returns>A non-generic enumerator for the registered dependency descriptors.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public void RegisterBackgroundWork(Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(implementationType);

        if (!typeof(ILiteBusBackgroundWork).IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"Type '{implementationType.FullName ?? implementationType.Name}' must implement {nameof(ILiteBusBackgroundWork)}.",
                nameof(implementationType));
        }

        if (!_registeredBackgroundWork.Add(implementationType))
        {
            return;
        }

        _services.Add(ServiceDescriptor.Singleton(implementationType, implementationType));
        _services.Add(ServiceDescriptor.Singleton<IHostedService>(serviceProvider =>
            new LiteBusBackgroundWorkHostedService(
                (ILiteBusBackgroundWork)serviceProvider.GetRequiredService(implementationType))));
    }

    /// <summary>
    ///     Converts a LiteBus dependency descriptor to a Microsoft DI service descriptor.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor to convert.</param>
    /// <returns>A Microsoft DI service descriptor.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when the descriptor is invalid (missing Instance, Factory, or ImplementationType).
    /// </exception>
    private static ServiceDescriptor ConvertToServiceDescriptor(DependencyDescriptor descriptor)
    {
        var serviceLifetime = ConvertLifetime(descriptor.Lifetime);

        if (descriptor.Instance is not null)
        {
            return new ServiceDescriptor(descriptor.DependencyType, descriptor.Instance);
        }

        if (descriptor.Factory is not null)
        {
            return new ServiceDescriptor(descriptor.DependencyType, descriptor.Factory, serviceLifetime);
        }

        if (descriptor.ImplementationType is not null)
        {
            return new ServiceDescriptor(descriptor.DependencyType, descriptor.ImplementationType, serviceLifetime);
        }

        throw new ArgumentException(
            "Invalid dependency descriptor: must have either Instance, Factory, or ImplementationType.",
            nameof(descriptor));
    }

    /// <summary>
    ///     Converts a LiteBus instance lifetime to a Microsoft DI service lifetime.
    /// </summary>
    /// <param name="lifetime">The LiteBus instance lifetime.</param>
    /// <returns>The corresponding Microsoft DI service lifetime.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when an unknown instance lifetime is provided.
    /// </exception>
    private static ServiceLifetime ConvertLifetime(InstanceLifetime lifetime)
    {
        return lifetime switch
        {
            InstanceLifetime.Transient => ServiceLifetime.Transient,
            InstanceLifetime.Singleton => ServiceLifetime.Singleton,
            _                          => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unknown instance lifetime.")
        };
    }
}