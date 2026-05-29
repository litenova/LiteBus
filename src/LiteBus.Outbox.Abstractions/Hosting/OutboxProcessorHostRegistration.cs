namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Marks that outbox processor hosting was configured during module registration.
/// </summary>
/// <remarks>
///     <para>
///         The outbox module stores this object in module configuration context when
///         <see cref="OutboxModuleBuilder.UseProcessorHost" /> is called. Hosting integration modules read the context to
///         register background services and health checks.
///     </para>
/// </remarks>
public sealed class OutboxProcessorHostRegistration
{
    /// <summary>
    ///     Gets the host options configured for the outbox processor loop.
    /// </summary>
    public required OutboxProcessorHostOptions HostOptions { get; init; }
}
