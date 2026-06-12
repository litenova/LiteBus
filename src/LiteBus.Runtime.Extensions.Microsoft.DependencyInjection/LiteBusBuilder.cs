using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Extensions.Microsoft.DependencyInjection;

/// <summary>
///     Default implementation of <see cref="ILiteBusBuilder" /> that collects shared contracts
///     and module registrations for the <see cref="ILiteBusBuilder" />-based
///     <c>AddLiteBus</c> overload on <see cref="ServiceCollectionExtensions" />.
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
        Modules = modules ?? throw new ArgumentNullException(nameof(modules));
        _sharedContracts = sharedContracts ?? throw new ArgumentNullException(nameof(sharedContracts));
    }

    /// <inheritdoc />
    public IContractWriter Contracts => _sharedContracts;

    /// <inheritdoc />
    public IModuleRegistry Modules { get; }

}