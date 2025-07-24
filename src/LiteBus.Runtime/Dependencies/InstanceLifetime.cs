namespace LiteBus.Runtime.Dependencies;

/// <summary>
/// Specifies the lifetime of a dependency registration in the dependency injection container.
/// </summary>
public enum InstanceLifetime
{
    /// <summary>
    /// A new instance is created each time the dependency is requested.
    /// </summary>
    Transient,

    /// <summary>
    /// A single instance is created and shared for the entire application lifetime.
    /// </summary>
    Singleton
}