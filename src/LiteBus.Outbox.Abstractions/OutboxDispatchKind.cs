namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Names which delivery path dispatches an outbox envelope.
/// </summary>
public enum OutboxDispatchKind
{
    /// <summary>
    ///     The background processor pass dispatched the envelope after leasing it from the store.
    /// </summary>
    Background = 0,

    /// <summary>
    ///     Immediate dispatch delivered the envelope after the staging mediation committed, in either await or
    ///     background mode.
    /// </summary>
    Immediate = 1,
}
