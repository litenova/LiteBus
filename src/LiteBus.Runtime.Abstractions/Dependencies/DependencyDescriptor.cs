using System;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Describes how a dependency should be registered in the dependency injection container.
///     This is LiteBus's abstraction over dependency registration that can be translated
///     to different DI container formats.
/// </summary>
public sealed class DependencyDescriptor : IEquatable<DependencyDescriptor>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DependencyDescriptor" /> class for transient registration.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="implementationType">The implementation type for the dependency.</param>
    /// <exception cref="System.ArgumentNullException">
    ///     Thrown when <paramref name="dependencyType" /> or <paramref name="implementationType" /> is <see langword="null" />
    ///     .
    /// </exception>
    public DependencyDescriptor(Type dependencyType, Type implementationType)
        : this(dependencyType, implementationType, InstanceLifetime.Transient)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DependencyDescriptor" /> class for type registration with an explicit
    ///     lifetime.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="implementationType">The implementation type for the dependency.</param>
    /// <param name="lifetime">The instance lifetime for resolved instances.</param>
    /// <exception cref="System.ArgumentNullException">
    ///     Thrown when <paramref name="dependencyType" /> or <paramref name="implementationType" /> is <see langword="null" />
    ///     .
    /// </exception>
    public DependencyDescriptor(Type dependencyType, Type implementationType, InstanceLifetime lifetime)
        : this(dependencyType, implementationType, lifetime, false)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DependencyDescriptor" /> class with explicit collection metadata.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="implementationType">The implementation type for the dependency.</param>
    /// <param name="lifetime">The instance lifetime for resolved instances.</param>
    /// <param name="isCollectionRegistration">
    ///     When <see langword="true" />, the descriptor participates in multi-registration collection resolution.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    ///     Thrown when <paramref name="dependencyType" /> or <paramref name="implementationType" /> is <see langword="null" />
    ///     .
    /// </exception>
    internal DependencyDescriptor(
        Type dependencyType,
        Type implementationType,
        InstanceLifetime lifetime,
        bool isCollectionRegistration)
    {
        ArgumentNullException.ThrowIfNull(dependencyType);
        ArgumentNullException.ThrowIfNull(implementationType);
        ValidateLifetime(lifetime);
        ValidateTypeRegistration(dependencyType, implementationType);

        DependencyType = dependencyType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
        IsCollectionRegistration = isCollectionRegistration;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DependencyDescriptor" /> class for singleton instance registration.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="instance">The singleton instance to register.</param>
    /// <exception cref="System.ArgumentNullException">
    ///     Thrown when <paramref name="dependencyType" /> or <paramref name="instance" /> is <see langword="null" />.
    /// </exception>
    public DependencyDescriptor(Type dependencyType, object instance)
        : this(dependencyType, instance, false)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DependencyDescriptor" /> class for singleton instance registration.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="instance">The singleton instance to register.</param>
    /// <param name="isCollectionRegistration">
    ///     When <see langword="true" />, the descriptor participates in multi-registration collection resolution.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    ///     Thrown when <paramref name="dependencyType" /> or <paramref name="instance" /> is <see langword="null" />.
    /// </exception>
    internal DependencyDescriptor(Type dependencyType, object instance, bool isCollectionRegistration)
    {
        ArgumentNullException.ThrowIfNull(dependencyType);
        ArgumentNullException.ThrowIfNull(instance);

        if (!dependencyType.IsInstanceOfType(instance))
        {
            throw new ArgumentException(
                $"Instance type '{instance.GetType().FullName ?? instance.GetType().Name}' is not assignable to " +
                $"service type '{dependencyType.FullName ?? dependencyType.Name}'.",
                nameof(instance));
        }

        DependencyType = dependencyType;
        Instance = instance;
        Lifetime = InstanceLifetime.Singleton;
        IsCollectionRegistration = isCollectionRegistration;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DependencyDescriptor" /> class for factory registration.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="factory">The factory function that creates instances of the dependency.</param>
    /// <exception cref="System.ArgumentNullException">
    ///     Thrown when <paramref name="dependencyType" /> or <paramref name="factory" /> is <see langword="null" />.
    /// </exception>
    public DependencyDescriptor(Type dependencyType, Func<IServiceProvider, object> factory)
        : this(dependencyType, factory, InstanceLifetime.Transient)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DependencyDescriptor" /> class for factory registration with an
    ///     explicit lifetime.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="factory">The factory function that creates instances of the dependency.</param>
    /// <param name="lifetime">The instance lifetime for factory-created instances.</param>
    /// <exception cref="System.ArgumentNullException">
    ///     Thrown when <paramref name="dependencyType" /> or <paramref name="factory" /> is <see langword="null" />.
    /// </exception>
    public DependencyDescriptor(
        Type dependencyType,
        Func<IServiceProvider, object> factory,
        InstanceLifetime lifetime)
        : this(dependencyType, factory, lifetime, false)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="DependencyDescriptor" /> class for factory registration with explicit
    ///     metadata.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="factory">The factory function that creates instances of the dependency.</param>
    /// <param name="lifetime">The instance lifetime for factory-created instances.</param>
    /// <param name="isCollectionRegistration">
    ///     When <see langword="true" />, the descriptor participates in multi-registration collection resolution.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    ///     Thrown when <paramref name="dependencyType" /> or <paramref name="factory" /> is <see langword="null" />.
    /// </exception>
    internal DependencyDescriptor(
        Type dependencyType,
        Func<IServiceProvider, object> factory,
        InstanceLifetime lifetime,
        bool isCollectionRegistration)
    {
        ArgumentNullException.ThrowIfNull(dependencyType);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateLifetime(lifetime);

        DependencyType = dependencyType;
        Factory = factory;
        Lifetime = lifetime;
        IsCollectionRegistration = isCollectionRegistration;
    }

    /// <summary>
    ///     Gets the dependency type that will be registered.
    /// </summary>
    /// <value>The type that will be used to resolve the dependency from the container.</value>
    public Type DependencyType { get; }

    /// <summary>
    ///     Gets the implementation type for the dependency, if applicable.
    /// </summary>
    /// <value>The concrete type that implements the dependency, or null for instance/factory registrations.</value>
    public Type? ImplementationType { get; }

    /// <summary>
    ///     Gets the singleton instance for the dependency, if applicable.
    /// </summary>
    /// <value>The singleton instance to register, or null for type/factory registrations.</value>
    public object? Instance { get; }

    /// <summary>
    ///     Gets the factory function for creating dependency instances, if applicable.
    /// </summary>
    /// <value>The factory function that creates dependency instances, or null for type/instance registrations.</value>
    public Func<IServiceProvider, object>? Factory { get; }

    /// <summary>
    ///     Gets the lifetime of the dependency registration.
    /// </summary>
    /// <value>The instance lifetime that determines how instances are created and managed.</value>
    public InstanceLifetime Lifetime { get; }

    /// <summary>
    ///     Gets a value indicating whether this descriptor registers one item in a multi-registration collection.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> when the descriptor was created for <see cref="IDependencyRegistry.RegisterCollection" />;
    ///     otherwise, <see langword="false" />.
    /// </value>
    public bool IsCollectionRegistration { get; }

    /// <summary>
    ///     Determines whether the specified <see cref="DependencyDescriptor" /> is equal to the current instance.
    ///     Type registrations compare dependency type, implementation type, and lifetime; instance and factory registrations
    ///     compare dependency type, lifetime, and the registered instance or factory reference.
    /// </summary>
    /// <param name="other">The descriptor to compare with the current instance.</param>
    /// <returns>
    ///     <see langword="true" /> if the specified descriptor is equal to the current instance; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    public bool Equals(DependencyDescriptor? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (DependencyType != other.DependencyType)
        {
            return false;
        }

        if (Lifetime != other.Lifetime)
        {
            return false;
        }

        if (IsCollectionRegistration != other.IsCollectionRegistration)
        {
            return false;
        }

        if (ImplementationType is not null || other.ImplementationType is not null)
        {
            return ImplementationType == other.ImplementationType;
        }

        if (Instance is not null || other.Instance is not null)
        {
            return ReferenceEquals(Instance, other.Instance);
        }

        if (Factory is not null || other.Factory is not null)
        {
            return ReferenceEquals(Factory, other.Factory);
        }

        return true;
    }

    /// <summary>
    ///     Creates a collection-registration descriptor for a concrete implementation type.
    /// </summary>
    /// <param name="dependencyType">The service type resolved as <c>IEnumerable&lt;T&gt;</c>.</param>
    /// <param name="implementationType">The implementation type registered for the collection.</param>
    /// <param name="lifetime">The instance lifetime for resolved instances.</param>
    /// <returns>A descriptor marked for collection registration.</returns>
    public static DependencyDescriptor ForCollection(
        Type dependencyType,
        Type implementationType,
        InstanceLifetime lifetime = InstanceLifetime.Transient)
    {
        return new DependencyDescriptor(dependencyType, implementationType, lifetime, true);
    }

    /// <summary>
    ///     Creates a collection-registration descriptor for a pre-created singleton instance.
    /// </summary>
    /// <param name="dependencyType">The service type resolved as <c>IEnumerable&lt;T&gt;</c>.</param>
    /// <param name="instance">The singleton instance to register in the collection.</param>
    /// <returns>A descriptor marked for collection registration.</returns>
    public static DependencyDescriptor ForCollection(Type dependencyType, object instance)
    {
        return new DependencyDescriptor(dependencyType, instance, true);
    }

    /// <summary>
    ///     Creates a collection-registration descriptor for a factory-created instance.
    /// </summary>
    /// <param name="dependencyType">The service type resolved as <c>IEnumerable&lt;T&gt;</c>.</param>
    /// <param name="factory">The factory that creates collection item instances.</param>
    /// <param name="lifetime">The instance lifetime for factory-created instances.</param>
    /// <returns>A descriptor marked for collection registration.</returns>
    public static DependencyDescriptor ForCollection(
        Type dependencyType,
        Func<IServiceProvider, object> factory,
        InstanceLifetime lifetime = InstanceLifetime.Transient)
    {
        return new DependencyDescriptor(dependencyType, factory, lifetime, true);
    }

    /// <summary>
    ///     Determines whether the specified object is equal to the current instance.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns>
    ///     <see langword="true" /> if the specified object is equal to the current instance; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as DependencyDescriptor);
    }

    /// <summary>
    ///     Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode()
    {
        if (ImplementationType is not null)
        {
            return HashCode.Combine(DependencyType, ImplementationType, Lifetime, IsCollectionRegistration);
        }

        if (Instance is not null)
        {
            return HashCode.Combine(DependencyType, Instance, Lifetime, IsCollectionRegistration);
        }

        if (Factory is not null)
        {
            return HashCode.Combine(DependencyType, Factory, Lifetime, IsCollectionRegistration);
        }

        return HashCode.Combine(DependencyType, Lifetime, IsCollectionRegistration);
    }

    /// <summary>
    ///     Determines whether two specified instances of <see cref="DependencyDescriptor" /> are equal.
    /// </summary>
    /// <param name="left">The first descriptor to compare.</param>
    /// <param name="right">The second descriptor to compare.</param>
    /// <returns><see langword="true" /> if the descriptors are equal; otherwise, <see langword="false" />.</returns>
    public static bool operator ==(DependencyDescriptor? left, DependencyDescriptor? right)
    {
        return Equals(left, right);
    }

    /// <summary>
    ///     Determines whether two specified instances of <see cref="DependencyDescriptor" /> are not equal.
    /// </summary>
    /// <param name="left">The first descriptor to compare.</param>
    /// <param name="right">The second descriptor to compare.</param>
    /// <returns><see langword="true" /> if the descriptors are not equal; otherwise, <see langword="false" />.</returns>
    public static bool operator !=(DependencyDescriptor? left, DependencyDescriptor? right)
    {
        return !Equals(left, right);
    }

    /// <summary>
    ///     Validates an instance lifetime supplied by a public descriptor constructor.
    /// </summary>
    /// <param name="lifetime">The lifetime value to validate.</param>
    private static void ValidateLifetime(InstanceLifetime lifetime)
    {
        if (!Enum.IsDefined(lifetime))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "The instance lifetime is not defined.");
        }
    }

    /// <summary>
    ///     Validates that an implementation type is concrete and compatible with its service type.
    /// </summary>
    /// <param name="dependencyType">The service type exposed to consumers.</param>
    /// <param name="implementationType">The implementation type created by the container.</param>
    private static void ValidateTypeRegistration(Type dependencyType, Type implementationType)
    {
        if (!implementationType.IsClass || implementationType.IsAbstract)
        {
            throw new ArgumentException(
                $"Implementation type '{implementationType.FullName ?? implementationType.Name}' must be a concrete class.",
                nameof(implementationType));
        }

        var isCompatible = dependencyType.IsGenericTypeDefinition
            ? IsOpenGenericImplementation(dependencyType, implementationType)
            : dependencyType.IsAssignableFrom(implementationType);

        if (!isCompatible)
        {
            throw new ArgumentException(
                $"Implementation type '{implementationType.FullName ?? implementationType.Name}' is not assignable to " +
                $"service type '{dependencyType.FullName ?? dependencyType.Name}'.",
                nameof(implementationType));
        }
    }

    /// <summary>
    ///     Determines whether an open generic implementation closes an open generic service contract.
    /// </summary>
    /// <param name="dependencyType">The open generic service contract.</param>
    /// <param name="implementationType">The candidate open generic implementation.</param>
    /// <returns><see langword="true" /> when the implementation closes the contract.</returns>
    private static bool IsOpenGenericImplementation(Type dependencyType, Type implementationType)
    {
        if (!implementationType.IsGenericTypeDefinition)
        {
            return false;
        }

        if (dependencyType == implementationType)
        {
            return true;
        }

        foreach (var interfaceType in implementationType.GetInterfaces())
        {
            if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == dependencyType)
            {
                return true;
            }
        }

        for (var baseType = implementationType.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == dependencyType)
            {
                return true;
            }
        }

        return false;
    }
}
