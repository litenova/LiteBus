namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Defines a module that can be registered with LiteBus to configure dependencies and message handling.
///     Modules provide a way to organize and encapsulate related functionality within the LiteBus framework.
/// </summary>
public interface IModule
{
    /// <summary>
    ///     Builds the module by configuring dependencies and registering components.
    ///     This method is called during the LiteBus initialization process.
    /// </summary>
    /// <param name="configuration">
    ///     The module configuration that provides access to dependency registration and message
    ///     registry.
    /// </param>
    void Build(IModuleConfiguration configuration);
}