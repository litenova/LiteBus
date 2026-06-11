using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Extensions.Microsoft.DependencyInjection;

/// <summary>
///     Fluent builder for configuring LiteBus modules and shared message contracts.
/// </summary>
public interface ILiteBusBuilder
{
    /// <summary>
    ///     Gets the shared contract writer. Registrations are applied to every module that uses
    ///     <see cref="IMessageContractRegistry" /> after all modules finish building.
    /// </summary>
    /// <value>The deferred contract writer for cross-module contract registration.</value>
    IContractWriter Contracts { get; }

    /// <summary>
    ///     Gets the module registry used to register LiteBus modules.
    /// </summary>
    /// <value>The module registry passed to <c>AddLiteBus</c> configuration.</value>
    IModuleRegistry Modules { get; }
}