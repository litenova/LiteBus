using System;
using System.Collections;
using System.Collections.Generic;
using Autofac;
using Autofac.Builder;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.Extensions.Autofac;

/// <summary>
/// Adapter that bridges LiteBus dependency registration with the Autofac container,
/// handling duplicate registrations gracefully using descriptor equality.
/// </summary>
internal sealed class AutofacDependencyRegistryAdapter : IDependencyRegistry
{
    private readonly ContainerBuilder _builder;
    private readonly HashSet<DependencyDescriptor> _registeredDescriptors = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="AutofacDependencyRegistryAdapter"/> class.
    /// </summary>
    /// <param name="builder">The Autofac container builder to register services with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    public AutofacDependencyRegistryAdapter(ContainerBuilder builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <summary>
    /// Gets the number of unique dependency descriptors that have been registered.
    /// </summary>
    public int Count => _registeredDescriptors.Count;

    /// <summary>
    /// Registers a dependency descriptor with the underlying Autofac container builder if not already registered.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Duplicate descriptors are silently ignored based on the descriptor's equality implementation.
    /// This prevents duplicate service registrations when multiple modules attempt to register the same services.
    /// </remarks>
    public void Register(DependencyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // Use HashSet.Add which leverages IEquatable<DependencyDescriptor>
        // Returns false if the descriptor is already present
        if (!_registeredDescriptors.Add(descriptor))
        {
            // Descriptor already registered, skip silently
            return;
        }

        ConvertToAutofacRegistration(descriptor);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the registered dependency descriptors.
    /// </summary>
    /// <returns>An enumerator for the registered dependency descriptors.</returns>
    public IEnumerator<DependencyDescriptor> GetEnumerator()
    {
        return _registeredDescriptors.GetEnumerator();
    }

    /// <summary>
    /// Returns a non-generic enumerator that iterates through the registered dependency descriptors.
    /// </summary>
    /// <returns>A non-generic enumerator for the registered dependency descriptors.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Converts a LiteBus dependency descriptor to an Autofac registration.
    /// </summary>
    /// <param name="descriptor">The dependency descriptor to convert.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the descriptor is invalid (missing Instance, Factory, or ImplementationType).
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
            // Note: Autofac's IComponentContext can resolve IServiceProvider if needed,
            // but direct resolution is more common. We adapt to IServiceProvider for consistency.
            registration = _builder.Register(c => descriptor.Factory(c.Resolve<IServiceProvider>()))
                .As(descriptor.DependencyType);
        }
        else if (descriptor.ImplementationType is not null)
        {
            registration = _builder.RegisterType(descriptor.ImplementationType)
                .As(descriptor.DependencyType);
        }
        else
        {
            throw new ArgumentException("Invalid dependency descriptor: must have either Instance, Factory, or ImplementationType.", nameof(descriptor));
        }

        // Apply lifetime
        switch (descriptor.Lifetime)
        {
            case InstanceLifetime.Singleton:
                registration.SingleInstance();
                break;
            case InstanceLifetime.Transient:
                registration.InstancePerDependency();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(descriptor.Lifetime), descriptor.Lifetime, "Unknown instance lifetime.");
        }
    }
}