using System.Linq;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Registry for managing module registration and providing dependency-ordered enumeration of modules.
/// </summary>
public interface IModuleRegistry : IOrderedEnumerable<ModuleDescriptor>
{
    /// <summary>
    ///     Registers a module in the registry.
    /// </summary>
    /// <param name="module">The module to register.</param>
    /// <returns>The current registry instance for method chaining.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when module is null.</exception>
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
}