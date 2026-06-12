namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Describes whether an inbox accept operation created a new row or returned an existing one.
/// </summary>
public enum InboxAcceptOutcome
{
    /// <summary>
    ///     The store accepted a new inbox envelope for the supplied accept item.
    /// </summary>
    Accepted = 0,

    /// <summary>
    ///     The store returned an existing inbox envelope for the supplied idempotency key or message identifier.
    /// </summary>
    AlreadyAccepted = 1
}
