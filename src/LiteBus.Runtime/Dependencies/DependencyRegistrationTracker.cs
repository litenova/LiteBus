using System;
using System.Collections;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Runtime.Dependencies;

/// <summary>
///     Tracks dependency descriptors and enforces single-registration conflict rules shared by
///     container adapters and the in-memory <see cref="DependencyRegistry" />.
/// </summary>
internal sealed class DependencyRegistrationTracker : IEnumerable<DependencyDescriptor>
{
    /// <summary>
    ///     Tracks the first descriptor registered for each service type so conflicting module registrations fail early.
    /// </summary>
    private readonly Dictionary<Type, DependencyDescriptor> _descriptorsByServiceType = [];

    /// <summary>
    ///     Tracks descriptors accepted by the registration policy.
    /// </summary>
    private readonly HashSet<DependencyDescriptor> _registeredDescriptors = [];

    /// <summary>
    ///     Gets the number of unique dependency descriptors tracked by this policy.
    /// </summary>
    public int Count => _registeredDescriptors.Count;

    /// <summary>
    ///     Applies registration policy for a descriptor and records it when accepted.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor to track.</param>
    /// <param name="enforceSingleRegistration">
    ///     When <see langword="true" />, rejects a second binding for the same
    ///     <see cref="DependencyDescriptor.DependencyType" />.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> when the descriptor is newly tracked and should be translated into the container;
    ///     <see langword="false" /> when an equal descriptor was already tracked.
    /// </returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when another module already registered <see cref="DependencyDescriptor.DependencyType" /> with a
    ///     different binding.
    /// </exception>
    public bool TryTrack(DependencyDescriptor descriptor, bool enforceSingleRegistration)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (enforceSingleRegistration &&
            _descriptorsByServiceType.TryGetValue(descriptor.DependencyType, out var existing))
        {
            if (existing.Equals(descriptor))
            {
                return false;
            }

            throw new LiteBusConfigurationException(
                $"Service type '{descriptor.DependencyType.FullName ?? descriptor.DependencyType.Name}' is already registered. " +
                "Each LiteBus module may register a given service type only once. Remove the duplicate registration or consolidate modules.");
        }

        if (enforceSingleRegistration)
        {
            _descriptorsByServiceType[descriptor.DependencyType] = descriptor;
        }

        return _registeredDescriptors.Add(descriptor);
    }

    /// <inheritdoc />
    public IEnumerator<DependencyDescriptor> GetEnumerator()
    {
        return _registeredDescriptors.GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
