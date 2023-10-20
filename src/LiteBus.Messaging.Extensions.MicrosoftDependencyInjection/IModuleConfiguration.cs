using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

/// <summary>
/// Represents the configuration for a module within an application.
/// </summary>
public interface IModuleConfiguration
{
    /// <summary>
    /// Gets the collection of services associated with the module configuration.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Gets the message registry associated with the module configuration.
    /// </summary>
    IMessageRegistry MessageRegistry { get; }
}