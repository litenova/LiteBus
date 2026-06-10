using System;
using System.Collections;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Runtime.Dependencies;

/// <summary>
///     Default implementation of <see cref="IDependencyRegistry" /> that manages dependency descriptors
///     used throughout LiteBus. This registry provides an abstraction over dependency injection containers
///     and rejects conflicting service registrations at configuration time.
/// </summary>
public sealed class DependencyRegistry : IDependencyRegistry
{
    /// <summary>
    ///     Stores unique dependency descriptors registered through this registry.
    /// </summary>
    private readonly HashSet<DependencyDescriptor> _descriptors = [];

    /// <summary>
    ///     Tracks the first descriptor registered for each service type so conflicting module registrations fail early.
    /// </summary>
    private readonly Dictionary<Type, DependencyDescriptor> _descriptorsByServiceType = [];

    /// <summary>
    ///     Gets the total number of dependency descriptors registered in the registry.
    /// </summary>
    /// <value>The total count of registered dependency descriptors.</value>
    public int Count => _descriptors.Count;

    /// <summary>
    ///     Registers a dependency in the registry when no other module has registered the same service type.
    ///     Duplicate registrations with equal descriptors are ignored; see <see cref="DependencyDescriptor.Equals(DependencyDescriptor?)" />.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor that defines how the dependency should be registered.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when another module already registered <see cref="DependencyDescriptor.DependencyType" /> with a different binding.
    /// </exception>
    public void Register(DependencyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.IsCollectionRegistration)
        {
            throw new LiteBusConfigurationException(
                $"Service type '{descriptor.DependencyType.FullName ?? descriptor.DependencyType.Name}' was registered with collection metadata. " +
                $"Use {nameof(RegisterCollection)} for multi-registration services such as IEnumerable<T> hooks.");
        }

        RegisterCore(descriptor, enforceSingleRegistration: true);
    }

    /// <inheritdoc />
    public void RegisterCollection(DependencyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!descriptor.IsCollectionRegistration)
        {
            throw new LiteBusConfigurationException(
                $"Service type '{descriptor.DependencyType.FullName ?? descriptor.DependencyType.Name}' must be created with " +
                $"{nameof(DependencyDescriptor.ForCollection)} before calling {nameof(RegisterCollection)}.");
        }

        RegisterCore(descriptor, enforceSingleRegistration: false);
    }

    /// <summary>
    ///     Adds a descriptor to the registry and applies single-registration conflict rules when required.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor to register.</param>
    /// <param name="enforceSingleRegistration">
    ///     When <see langword="true" />, rejects a second binding for the same <see cref="DependencyDescriptor.DependencyType" />.
    /// </param>
    private void RegisterCore(DependencyDescriptor descriptor, bool enforceSingleRegistration)
    {
        if (enforceSingleRegistration &&
            _descriptorsByServiceType.TryGetValue(descriptor.DependencyType, out var existing))
        {
            if (existing.Equals(descriptor))
            {
                return;
            }

            throw new LiteBusConfigurationException(
                $"Service type '{descriptor.DependencyType.FullName ?? descriptor.DependencyType.Name}' is already registered. " +
                "Each LiteBus module may register a given service type only once. Remove the duplicate registration or consolidate modules.");
        }

        if (enforceSingleRegistration)
        {
            _descriptorsByServiceType[descriptor.DependencyType] = descriptor;
        }

        if (!_descriptors.Add(descriptor))
        {
            return;
        }
    }

    /// <summary>
    ///     Returns an enumerator that iterates through the dependency descriptors.
    /// </summary>
    /// <returns>An enumerator that can be used to iterate through the dependency descriptors.</returns>
    public IEnumerator<DependencyDescriptor> GetEnumerator()
    {
        return _descriptors.GetEnumerator();
    }

    /// <summary>
    ///     Returns an enumerator that iterates through the dependency descriptors.
    /// </summary>
    /// <returns>An enumerator that can be used to iterate through the dependency descriptors.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}