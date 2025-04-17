namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

/// <summary>
///     Represents a module in an application that can be configured and built.
/// </summary>
public interface IModule
{
    /// <summary>
    ///     Builds the module using the provided configuration.
    /// </summary>
    /// <param name="configuration">The configuration for the module.</param>
    void Build(IModuleConfiguration configuration);
}