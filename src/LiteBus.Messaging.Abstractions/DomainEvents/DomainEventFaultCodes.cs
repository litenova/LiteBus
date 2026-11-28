namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The fault codes the domain event staging handler reports when a domain event decision cannot be honored.
/// </summary>
/// <remarks>
///     <para>
///         Each code is carried on a <see cref="MessageFaultException" /> raised before the unit of work commits, so
///         the business write never commits without its declared consequences. A missing decision, an unregistered
///         translator, an unusable translation result, and missing in-process publish infrastructure are configuration
///         or dependency failures an operator must fix, not refusals the caller can act on. The mediation reports
///         <see cref="MediationOutcome.Failed" /> with the code, the fault is logged, and transports answer it as a
///         server error.
///     </para>
/// </remarks>
public static class DomainEventFaultCodes
{
    /// <summary>
    ///     The fault code reported when a drained domain event has no registered decision and the undecided policy is <see cref="UndecidedDomainEventPolicy.Throw" />.
    /// </summary>
    public const string UndecidedDomainEvent = "domain-events.undecided-event";

    /// <summary>
    ///     The fault code reported when the translator type registered for a domain event is not registered in dependency injection.
    /// </summary>
    public const string TranslatorMissing = "domain-events.translator-missing";

    /// <summary>
    ///     The fault code reported when a translator returns a null list or a list containing a null message.
    /// </summary>
    public const string InvalidTranslation = "domain-events.invalid-translation";

    /// <summary>
    ///     The fault code reported when a domain event must publish in process but no event mediator is registered.
    /// </summary>
    public const string EventMediatorMissing = "domain-events.event-mediator-missing";

    /// <summary>
    ///     The fault code reported when a domain event must publish after commit but the scoped buffer or the commit listener is not registered.
    /// </summary>
    public const string InProcessPublishUnavailable = "domain-events.in-process-publish-unavailable";
}
