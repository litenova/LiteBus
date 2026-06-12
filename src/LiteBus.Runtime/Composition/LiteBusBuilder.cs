using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.Composition;

/// <summary>
///     Default implementation of <see cref="ILiteBusBuilder" /> that collects shared contracts
///     and module registrations for <c>AddLiteBus</c> configuration callbacks.
/// </summary>
public sealed class LiteBusBuilder : ILiteBusBuilder
{
    /// <summary>
    ///     Deferred contract registrations applied after all modules build.
    /// </summary>
    private readonly MessageContractBuilder _sharedContracts;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusBuilder" /> class.
    /// </summary>
    /// <param name="modules">The module registry exposed to configuration callbacks.</param>
    /// <param name="sharedContracts">The shared contract builder populated during configuration.</param>
    public LiteBusBuilder(IModuleRegistry modules, MessageContractBuilder sharedContracts)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(sharedContracts);

        Modules = modules;
        _sharedContracts = sharedContracts;
    }

    /// <inheritdoc />
    public IContractWriter Contracts => _sharedContracts;

    /// <inheritdoc />
    public IModuleRegistry Modules { get; }
}
