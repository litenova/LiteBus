namespace LiteBus.Inbox.Dispatch.Amqp.IntegrationTests;

/// <summary>
///     Test command payload stored in the inbox and published to AMQP.
/// </summary>
public sealed record RemoteWorkCommand
{
    /// <summary>
    ///     Gets the work item identifier.
    /// </summary>
    public required Guid WorkItemId { get; init; }

    /// <summary>
    ///     Gets the optional idempotency key supplied by the caller.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
