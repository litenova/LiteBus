using System;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Defines a module that owns child modules as part of its configuration.
///     The registry expands children during registration and adds explicit ordering
///     edges before topological sorting.
/// </summary>
public interface ICompositeModule : IModule
{
    /// <summary>
    ///     Gets whether the parent or its children build first.
    /// </summary>
    /// <value><see cref="CompositeModuleBuildOrder.ParentFirst" /> by default.</value>
    CompositeModuleBuildOrder BuildOrder => CompositeModuleBuildOrder.ParentFirst;

    /// <summary>
    ///     Declares child modules by invoking <paramref name="registerChild" /> for
    ///     each one. The registry calls this method during <see cref="IModuleRegistry.Register" /> before any
    ///     <see cref="IModule.Build" /> call. The module's builder action must run inside this method so every child is
    ///     known before the graph is frozen.
    /// </summary>
    /// <param name="registerChild">Registers each child module with the module registry.</param>
    void DeclareChildren(Action<IModule> registerChild);
}
