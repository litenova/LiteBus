namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Defines the package-neutral composition surface exposed by host adapters.
/// </summary>
/// <remarks>
///     Feature packages extend this interface with registration methods. Runtime does not
///     reference messaging, durable messaging, storage, or transport packages.
/// </remarks>
public interface ILiteBusBuilder
{
    /// <summary>
    ///     Gets the module registry used by feature-specific composition extensions.
    /// </summary>
    /// <value>The registry that collects modules before graph validation and build.</value>
    IModuleRegistry Modules { get; }
}
