using System;
using System.Collections;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;
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
    ///     Tracks the first descriptor registered for each service type so conflicting module registrations fail early.
    /// </summary>
    private readonly Dictionary<Type, DependencyDescriptor> _descriptorsByServiceType = [];

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
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when another module already registered <see cref="DependencyDescriptor.DependencyType" /> with a different binding.
    /// </exception>
    /// <remarks>
    ///     Duplicate registrations with equal descriptors are ignored; see <see cref="DependencyDescriptor.Equals(DependencyDescriptor?)" />.
    /// </remarks>
    public void Register(DependencyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (_descriptorsByServiceType.TryGetValue(descriptor.DependencyType, out var existing))
        {
            if (existing.Equals(descriptor))
            {
                return;
            }

            throw new LiteBusConfigurationException(
                $"Service type '{descriptor.DependencyType.FullName ?? descriptor.DependencyType.Name}' is already registered. " +
                "Each LiteBus module may register a given service type only once. Remove the duplicate registration or consolidate modules.");
        }

        _descriptorsByServiceType[descriptor.DependencyType] = descriptor;
        _registeredDescriptors.Add(descriptor);

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
            InstanceLifetime.Scoped    => ServiceLifetime.Scoped,
            _                          => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unknown instance lifetime.")
        };
    }
}