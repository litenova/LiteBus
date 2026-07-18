using System.Collections.Generic;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Registry for managing module registration and resolving dependency-ordered module descriptors.
/// </summary>
public interface IModuleRegistry
{
    /// <summary>
    ///     Registers a module in the registry.
    /// </summary>
    /// <param name="module">The module to register.</param>
    /// <returns>The current registry instance for method chaining.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="module" /> is <see langword="null" />.</exception>
    /// <exception cref="Exceptions.LiteBusConfigurationException">
    ///     Thrown when registration is attempted after <see cref="BuildOrder" /> has frozen the registry.
    /// </exception>
    IModuleRegistry Register(IModule module);

    /// <summary>
    ///     Determines whether a module of the specified type has been registered in the module registry.
    /// </summary>
    /// <typeparam name="T">The type of module to check for registration. Must implement <see cref="IModule" />.</typeparam>
    /// <returns>
    ///     <see langword="true" /> if a module of type <typeparamref name="T" /> is registered; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     This method performs a type-based lookup to determine if a module has been registered.
    ///     It only checks for exact type matches and does not consider inheritance relationships.
    /// </remarks>
    /// <example>
    ///     <code>
    /// var moduleRegistry = new ModuleRegistry();
    /// moduleRegistry.Register(new MessageModule(_ => { }));
    /// 
    /// bool isRegistered = moduleRegistry.IsModuleRegistered&lt;MessageModule&gt;();
    /// // isRegistered will be true
    /// </code>
    /// </example>
    bool IsModuleRegistered<T>() where T : IModule;

    /// <summary>
    ///     Returns module descriptors in dependency order and freezes further registration.
    /// </summary>
    /// <returns>Module descriptors sorted so dependencies appear before dependents.</returns>
    /// <exception cref="Exceptions.LiteBusConfigurationException">
    ///     Thrown when circular dependencies are detected or when required dependencies are missing.
    /// </exception>
    IReadOnlyList<ModuleDescriptor> BuildOrder();
}
