using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.Composition;

/// <summary>
///     Default implementation of <see cref="ILiteBusBuilder" /> used by host adapters.
/// </summary>
public sealed class LiteBusBuilder : ILiteBusBuilder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusBuilder" /> class.
    /// </summary>
    /// <param name="modules">The module registry exposed to configuration callbacks.</param>
    public LiteBusBuilder(IModuleRegistry modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        Modules = modules;
    }

    /// <inheritdoc />
    public IModuleRegistry Modules { get; }
}
