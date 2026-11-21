namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Where the committed fact that caused a root mediation came from.
/// </summary>
public enum MediationCauseSource
{
    /// <summary>
    ///     An outbox envelope delivered by the outbox processor.
    /// </summary>
    Outbox = 0,

    /// <summary>
    ///     An outbox envelope delivered by immediate dispatch after staging.
    /// </summary>
    ImmediateDispatch = 1,

    /// <summary>
    ///     An inbox envelope accepted from a broker or transport.
    /// </summary>
    Inbox = 2,

    /// <summary>
    ///     A recurring command schedule firing.
    /// </summary>
    RecurringSchedule = 3,

    /// <summary>
    ///     A saga timeout that started the mediation.
    /// </summary>
    Saga = 4,

    /// <summary>
    ///     A domain event published in process after the unit of work committed.
    /// </summary>
    DomainEvent = 5
}
