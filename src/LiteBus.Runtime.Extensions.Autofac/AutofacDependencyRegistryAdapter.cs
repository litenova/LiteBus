using System;
using System.Collections;
using System.Collections.Generic;
using Autofac;
using Autofac.Builder;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Dependencies;

namespace LiteBus.Runtime.Extensions.Autofac;

/// <summary>
///     Adapter that bridges LiteBus dependency registration with the Autofac container,
///     handling duplicate registrations gracefully using descriptor equality.
/// </summary>
internal sealed class AutofacDependencyRegistryAdapter : IDependencyRegistry
{
    /// <summary>
    ///     The Autofac container builder receiving LiteBus dependency registrations.
    /// </summary>
    private readonly ContainerBuilder _builder;

    /// <summary>
    ///     Shared registration policy used to track descriptors and detect conflicts.
    /// </summary>
    private readonly DependencyRegistrationTracker _tracker = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="AutofacDependencyRegistryAdapter" /> class.
    /// </summary>
    /// <param name="builder">The Autofac container builder to register services with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder" /> is <see langword="null" />.</exception>
    public AutofacDependencyRegistryAdapter(ContainerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _builder = builder;
    }

    /// <summary>
    ///     Gets the number of unique dependency descriptors that have been registered.
    /// </summary>
    /// <value>The count of tracked dependency descriptors.</value>
    public int Count => _tracker.Count;

    /// <summary>
    ///     Registers a dependency descriptor with the underlying Autofac container builder if not already registered.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when another module already registered <see cref="DependencyDescriptor.DependencyType" /> with a different
    ///     binding.
    /// </exception>
    /// <remarks>
    ///     Duplicate registrations with equal descriptors are ignored; see
    ///     <see cref="DependencyDescriptor.Equals(DependencyDescriptor?)" />.
    /// </remarks>
    public void Register(DependencyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.IsCollectionRegistration)
        {
            throw new LiteBusConfigurationException(
                $"Service type '{descriptor.DependencyType.FullName ?? descriptor.DependencyType.Name}' was registered with collection metadata. " +
                $"Use {nameof(RegisterCollection)} for multi-registration services such as IEnumerable<T> hooks.");
        }

        RegisterCore(descriptor, true);
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

        RegisterCore(descriptor, false);
    }

    /// <summary>
    ///     Returns an enumerator that iterates through the registered dependency descriptors.
    /// </summary>
    /// <returns>An enumerator for the registered dependency descriptors.</returns>
    public IEnumerator<DependencyDescriptor> GetEnumerator()
    {
        return _tracker.GetEnumerator();
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
    ///     Applies shared registration policy and translates accepted descriptors into Autofac registrations.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor to register.</param>
    /// <param name="enforceSingleRegistration">
    ///     When <see langword="true" />, rejects a second binding for the same
    ///     <see cref="DependencyDescriptor.DependencyType" />.
    /// </param>
    private void RegisterCore(DependencyDescriptor descriptor, bool enforceSingleRegistration)
    {
        if (!_tracker.TryTrack(descriptor, enforceSingleRegistration))
        {
            return;
        }

        ConvertToAutofacRegistration(descriptor);
    }

    /// <summary>
    ///     Converts a LiteBus dependency descriptor to an Autofac registration.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor to convert.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when the descriptor is invalid (missing Instance, Factory, or ImplementationType).
    /// </exception>
    private void ConvertToAutofacRegistration(DependencyDescriptor descriptor)
    {
        IRegistrationBuilder<object, object, object> registration;

        if (descriptor.Instance is not null)
        {
            registration = _builder.RegisterInstance(descriptor.Instance)
                .As(descriptor.DependencyType);
        }
        else if (descriptor.Factory is not null)
        {
            registration = _builder.Register(c =>
                    descriptor.Factory(new AutofacServiceProviderAdapter(c.Resolve<ILifetimeScope>())))
                .As(descriptor.DependencyType);
        }
        else if (descriptor.ImplementationType is not null)
        {
            registration = descriptor.ImplementationType.IsGenericTypeDefinition
                ? _builder.RegisterGeneric(descriptor.ImplementationType).As(descriptor.DependencyType)
                : _builder.RegisterType(descriptor.ImplementationType).As(descriptor.DependencyType);
        }
        else
        {
            throw new ArgumentException("Invalid dependency descriptor: must have either Instance, Factory, or ImplementationType.", nameof(descriptor));
        }

        switch (descriptor.Lifetime)
        {
            case InstanceLifetime.Singleton:
                registration.SingleInstance();
                break;
            case InstanceLifetime.Transient:
                registration.InstancePerDependency();
                break;
            case InstanceLifetime.Scoped:
                registration.InstancePerLifetimeScope();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.Lifetime, "Unknown instance lifetime.");
        }
    }
}
