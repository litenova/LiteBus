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
    ///     Tracks service types registered as collection items so a later single binding for the same type fails.
    /// </summary>
    private readonly HashSet<Type> _collectionServiceTypes = [];

    /// <summary>
    ///     Tracks the first descriptor registered for each service type so conflicting module registrations fail early.
    /// </summary>
    private readonly Dictionary<Type, DependencyDescriptor> _descriptorsByServiceType = [];

    /// <summary>
    ///     Tracks descriptors accepted by the registration policy for constant-time duplicate detection.
    /// </summary>
    private readonly HashSet<DependencyDescriptor> _registeredDescriptors = [];

    /// <summary>
    ///     Preserves descriptors in first-registration order for deterministic container translation.
    /// </summary>
    private readonly List<DependencyDescriptor> _orderedDescriptors = [];

    /// <summary>
    ///     Tracks service types whose LiteBus default binding was replaced through <see cref="TryReplace" />.
    /// </summary>
    private readonly HashSet<Type> _replacedDefaultServiceTypes = [];

    /// <summary>
    ///     Gets the number of unique dependency descriptors tracked by this policy.
    /// </summary>
    public int Count => _orderedDescriptors.Count;

    /// <summary>
    ///     Gets the service types whose LiteBus default binding was replaced through <see cref="TryReplace" />.
    /// </summary>
    /// <value>One entry per replaced default, so composition reporting can name ports the application replaced.</value>
    /// <remarks>
    ///     A replacement overwrites the descriptor in place, so the descriptor list alone cannot tell a replaced
    ///     default from an explicit registration for a service LiteBus never defaulted. This history keeps that answer
    ///     for ports the composition model does not name itself.
    /// </remarks>
    public IReadOnlyCollection<Type> ReplacedDefaultServiceTypes => _replacedDefaultServiceTypes;

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
    ///     different binding. The message names both sources and points at <c>Replace</c> for a deliberate override.
    /// </exception>
    /// <exception cref="ModuleCompositionException">
    ///     Thrown when a single binding and a collection registration target the same service type.
    /// </exception>
    public bool TryTrack(DependencyDescriptor descriptor, bool enforceSingleRegistration)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (enforceSingleRegistration &&
            _descriptorsByServiceType.TryGetValue(descriptor.DependencyType, out var existing))
        {
            // A LiteBus default yields to whatever is already registered, so a store module that registered its own
            // port before the feature module registers the default keeps its binding.
            if (descriptor.IsLiteBusDefault && !existing.IsLiteBusDefault)
            {
                return false;
            }

            RegistrationConflict.ThrowIfConflicting(
                existing,
                descriptor,
                $"Service type '{descriptor.DependencyType.FullName ?? descriptor.DependencyType.Name}'",
                DescribeSource(existing),
                DescribeSource(descriptor));

            return false;
        }

        if (enforceSingleRegistration)
        {
            if (_collectionServiceTypes.Contains(descriptor.DependencyType))
            {
                throw new ModuleCompositionException(
                    $"Service type '{descriptor.DependencyType.FullName ?? descriptor.DependencyType.Name}' is already registered as a collection. " +
                    "Each LiteBus module may register a given service type either as a single binding or as collection items, not both. " +
                    "Remove the duplicate registration or consolidate modules.");
            }

            _descriptorsByServiceType[descriptor.DependencyType] = descriptor;
        }
        else
        {
            if (_descriptorsByServiceType.ContainsKey(descriptor.DependencyType))
            {
                throw new ModuleCompositionException(
                    $"Service type '{descriptor.DependencyType.FullName ?? descriptor.DependencyType.Name}' is already registered as a single binding. " +
                    "Each LiteBus module may register a given service type either as a single binding or as collection items, not both. " +
                    "Remove the duplicate registration or consolidate modules.");
            }

            _collectionServiceTypes.Add(descriptor.DependencyType);
        }

        if (!_registeredDescriptors.Add(descriptor))
        {
            return false;
        }

        _orderedDescriptors.Add(descriptor);
        return true;
    }

    /// <summary>
    ///     Replaces the tracked binding for a service type with a new descriptor.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor that overwrites the existing binding.</param>
    /// <returns>
    ///     <see langword="true" /> when the descriptor was newly tracked or overwrote a different binding;
    ///     <see langword="false" /> when an equal descriptor was already tracked.
    /// </returns>
    /// <exception cref="ModuleCompositionException">
    ///     Thrown when the service type is already registered as a collection.
    /// </exception>
    /// <remarks>
    ///     This is the deliberate override for the registration conflict rule. A conflicting repeat
    ///     through <see cref="TryTrack" /> fails composition, while this method overwrites it, which is
    ///     how an application replaces a LiteBus default port registration with an explicit implementation.
    ///     The replacement keeps the original registration position for deterministic container translation.
    /// </remarks>
    public bool TryReplace(DependencyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (_collectionServiceTypes.Contains(descriptor.DependencyType))
        {
            throw new ModuleCompositionException(
                $"Service type '{descriptor.DependencyType.FullName ?? descriptor.DependencyType.Name}' is already registered as a collection. " +
                "Each LiteBus module may register a given service type either as a single binding or as collection items, not both. " +
                "Remove the duplicate registration or consolidate modules.");
        }

        if (_descriptorsByServiceType.TryGetValue(descriptor.DependencyType, out var existing))
        {
            if (existing.Equals(descriptor))
            {
                return false;
            }

            if (existing.IsLiteBusDefault)
            {
                _replacedDefaultServiceTypes.Add(descriptor.DependencyType);
            }

            _registeredDescriptors.Remove(existing);
            _registeredDescriptors.Add(descriptor);
            _descriptorsByServiceType[descriptor.DependencyType] = descriptor;

            var index = _orderedDescriptors.IndexOf(existing);

            if (index >= 0)
            {
                _orderedDescriptors[index] = descriptor;
            }
            else
            {
                _orderedDescriptors.Add(descriptor);
            }

            return true;
        }

        _descriptorsByServiceType[descriptor.DependencyType] = descriptor;
        _registeredDescriptors.Add(descriptor);
        _orderedDescriptors.Add(descriptor);
        return true;
    }

    /// <summary>
    ///     Describes a descriptor binding and provenance for a conflict message.
    /// </summary>
    /// <param name="descriptor">The descriptor to describe.</param>
    /// <returns>A source description naming the binding and whether it is a LiteBus default.</returns>
    private static string DescribeSource(DependencyDescriptor descriptor)
    {
        string binding;

        if (descriptor.ImplementationType is not null)
        {
            binding = $"'{descriptor.ImplementationType.FullName ?? descriptor.ImplementationType.Name}'";
        }
        else if (descriptor.Instance is not null)
        {
            var instanceType = descriptor.Instance.GetType();
            binding = $"instance of '{instanceType.FullName ?? instanceType.Name}'";
        }
        else
        {
            binding = "factory registration";
        }

        return descriptor.IsLiteBusDefault
            ? $"LiteBus default {binding}"
            : $"explicit registration {binding}";
    }

    /// <inheritdoc />
    public IEnumerator<DependencyDescriptor> GetEnumerator()
    {
        return _orderedDescriptors.GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
