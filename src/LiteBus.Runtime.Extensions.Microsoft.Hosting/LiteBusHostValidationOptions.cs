namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Selects which checks host validation runs.
/// </summary>
/// <remarks>
///     Every check is on by default. Switch one off by setting its flag to <see langword="false" />. A host that
///     stages events only through explicit enqueue calls switches the event-consistency check off, and a host with
///     message types no sample factory can build switches the round-trip check off or reports those contracts as
///     not run through <see cref="FailOnMissingSample" />.
/// </remarks>
public sealed record LiteBusHostValidationOptions
{
    /// <summary>
    ///     Gets a value indicating whether every LiteBus host registration resolves from the root provider.
    /// </summary>
    /// <value><see langword="true" /> by default.</value>
    public bool ValidateRegistrations { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether every registered durable message contract survives a
    ///     serialize-and-deserialize round trip through the configured serializer with its values intact.
    /// </summary>
    /// <value><see langword="true" /> by default. Skipped silently when messaging was not composed.</value>
    public bool ValidateContractRoundTrips { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether every event the outbox stages has a registered message contract and a
    ///     subscribed handler.
    /// </summary>
    /// <value><see langword="true" /> by default. Skipped silently when messaging was not composed.</value>
    public bool ValidateEventConsistency { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether a contract no sample factory can sample fails validation.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> by default. When <see langword="false" />, the round trip for an unsampleable
    ///     contract is reported as not run on <see cref="LiteBusHostValidationResult.SkippedRoundTrips" /> instead
    ///     of failing the host. A factory exception and a serialization failure still fail either way.
    /// </value>
    public bool FailOnMissingSample { get; init; } = true;
}
