namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Marks that command inbox processor hosting was configured during module registration.
/// </summary>
/// <remarks>
///     <para>
///         The inbox module stores this object in module configuration context when
///         <see cref="CommandInboxModuleBuilder.UseProcessorHost" /> is called. Hosting integration modules read the
///         context to register background services and health checks.
///     </para>
/// </remarks>
public sealed class CommandInboxProcessorHostRegistration
{
    /// <summary>
    ///     Gets the host options configured for the command inbox processor loop.
    /// </summary>
    public required CommandInboxProcessorHostOptions HostOptions { get; init; }
}
