using System;

namespace LiteBus.Runtime.Dependencies;

/// <summary>
/// Describes how a dependency should be registered in the dependency injection container.
/// This is LiteBus's abstraction over dependency registration that can be translated
/// to different DI container formats.
/// </summary>
public sealed class DependencyDescriptor : IEquatable<DependencyDescriptor>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyDescriptor"/> class for transient registration.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="implementationType">The implementation type for the dependency.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="dependencyType"/> or <paramref name="implementationType"/> is null.
    /// </exception>
    public DependencyDescriptor(Type dependencyType, Type implementationType)
    {
        DependencyType = dependencyType ?? throw new ArgumentNullException(nameof(dependencyType));
        ImplementationType = implementationType ?? throw new ArgumentNullException(nameof(implementationType));
        Lifetime = InstanceLifetime.Transient;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyDescriptor"/> class for singleton instance registration.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="instance">The singleton instance to register.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="dependencyType"/> or <paramref name="instance"/> is null.
    /// </exception>
    public DependencyDescriptor(Type dependencyType, object instance)
    {
        DependencyType = dependencyType ?? throw new ArgumentNullException(nameof(dependencyType));
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        Lifetime = InstanceLifetime.Singleton;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyDescriptor"/> class for factory registration.
    /// </summary>
    /// <param name="dependencyType">The dependency type to register.</param>
    /// <param name="factory">The factory function that creates instances of the dependency.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="dependencyType"/> or <paramref name="factory"/> is null.
    /// </exception>
    public DependencyDescriptor(Type dependencyType, Func<IServiceProvider, object> factory)
    {
        DependencyType = dependencyType ?? throw new ArgumentNullException(nameof(dependencyType));
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Lifetime = InstanceLifetime.Transient;
    }

    /// <summary>
    /// Gets the dependency type that will be registered.
    /// </summary>
    /// <value>The type that will be used to resolve the dependency from the container.</value>
    public Type DependencyType { get; }

    /// <summary>
    /// Gets the implementation type for the dependency, if applicable.
    /// </summary>
    /// <value>The concrete type that implements the dependency, or null for instance/factory registrations.</value>
    public Type? ImplementationType { get; }

    /// <summary>
    /// Gets the singleton instance for the dependency, if applicable.
    /// </summary>
    /// <value>The singleton instance to register, or null for type/factory registrations.</value>
    public object? Instance { get; }

    /// <summary>
    /// Gets the factory function for creating dependency instances, if applicable.
    /// </summary>
    /// <value>The factory function that creates dependency instances, or null for type/instance registrations.</value>
    public Func<IServiceProvider, object>? Factory { get; }

    /// <summary>
    /// Gets the lifetime of the dependency registration.
    /// </summary>
    /// <value>The instance lifetime that determines how instances are created and managed.</value>
    public InstanceLifetime Lifetime { get; }

    /// <summary>
    /// Determines whether the specified <see cref="DependencyDescriptor"/> is equal to the current instance.
    /// Two descriptors are considered equal if they have the same dependency type and implementation type.
    /// </summary>
    /// <param name="other">The descriptor to compare with the current instance.</param>
    /// <returns><c>true</c> if the specified descriptor is equal to the current instance; otherwise, <c>false</c>.</returns>
    public bool Equals(DependencyDescriptor? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return DependencyType == other.DependencyType &&
               ImplementationType == other.ImplementationType;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current instance.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns><c>true</c> if the specified object is equal to the current instance; otherwise, <c>false</c>.</returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as DependencyDescriptor);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(DependencyType, ImplementationType);
    }

    /// <summary>
    /// Determines whether two specified instances of <see cref="DependencyDescriptor"/> are equal.
    /// </summary>
    /// <param name="left">The first descriptor to compare.</param>
    /// <param name="right">The second descriptor to compare.</param>
    /// <returns><c>true</c> if the descriptors are equal; otherwise, <c>false</c>.</returns>
    public static bool operator ==(DependencyDescriptor? left, DependencyDescriptor? right)
    {
        return Equals(left, right);
    }

    /// <summary>
    /// Determines whether two specified instances of <see cref="DependencyDescriptor"/> are not equal.
    /// </summary>
    /// <param name="left">The first descriptor to compare.</param>
    /// <param name="right">The second descriptor to compare.</param>
    /// <returns><c>true</c> if the descriptors are not equal; otherwise, <c>false</c>.</returns>
    public static bool operator !=(DependencyDescriptor? left, DependencyDescriptor? right)
    {
        return !Equals(left, right);
    }
}