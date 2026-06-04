using System;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     A module that owns child modules as part of its configuration.
///     The registry expands children into the ordered build sequence immediately
///     after the parent, before topological sort runs. Implement this interface
///     alongside <see cref="IModule" /> when a module owns sub-modules.
/// </summary>
public interface ICompositeModule : IModule
{
    /// <summary>
    ///     Declares child modules by invoking <paramref name="registerChild" /> for
    ///     each one. Called by the registry during
    ///     <see cref="IModuleRegistry.Register" /> before any
    ///     <see cref="IModule.Build" /> call.
    ///     The module's builder action MUST run inside this method so that child
    ///     modules are known at registration time.
    /// </summary>
    /// <param name="registerChild">Registers each child module with the module registry.</param>
    void DeclareChildren(Action<IModule> registerChild);
}
