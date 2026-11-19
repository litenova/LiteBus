using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     The per-type domain event decisions collected from the outbox builder, read by the staging completion handler.
/// </summary>
/// <remarks>
///     <para>
///         Each drained event is matched by exact runtime type against one decision: translate through a registered
///         translator, drop without staging, or publish in process after the commit. An event type with no decision
///         follows <see cref="UndecidedPolicy" />.
///     </para>
///     <para>
///         Registered once as a singleton by the outbox module and built from the builder before any mediation runs, so
///         decisions are fixed for the life of the composition.
///     </para>
/// </remarks>
internal sealed class DomainEventDecisions
{
    /// <summary>
    ///     The translator registrations by closed domain event type.
    /// </summary>
    private readonly Dictionary<Type, DomainEventTranslatorRegistration> _translators;

    /// <summary>
    ///     The domain event types dropped without staging.
    /// </summary>
    private readonly HashSet<Type> _ignored;

    /// <summary>
    ///     The domain event types published in process after the commit.
    /// </summary>
    private readonly HashSet<Type> _publishedInProcess;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DomainEventDecisions" /> class.
    /// </summary>
    /// <param name="translators">The translator registrations by closed domain event type.</param>
    /// <param name="ignored">The domain event types dropped without staging.</param>
    /// <param name="publishedInProcess">The domain event types published in process after the commit.</param>
    /// <param name="undecidedPolicy">What the staging handler does with a domain event type that has no registered decision.</param>
    internal DomainEventDecisions(
        IReadOnlyDictionary<Type, DomainEventTranslatorRegistration> translators,
        IReadOnlyCollection<Type> ignored,
        IReadOnlyCollection<Type> publishedInProcess,
        UndecidedDomainEventPolicy undecidedPolicy)
    {
        ArgumentNullException.ThrowIfNull(translators);
        ArgumentNullException.ThrowIfNull(ignored);
        ArgumentNullException.ThrowIfNull(publishedInProcess);

        if (!Enum.IsDefined(undecidedPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(undecidedPolicy),
                undecidedPolicy,
                "The undecided domain event policy must be a defined value.");
        }

        _translators = new Dictionary<Type, DomainEventTranslatorRegistration>(translators);
        _ignored = new HashSet<Type>(ignored);
        _publishedInProcess = new HashSet<Type>(publishedInProcess);
        UndecidedPolicy = undecidedPolicy;
    }

    /// <summary>
    ///     Gets what the staging handler does with a domain event type that has no registered decision.
    /// </summary>
    public UndecidedDomainEventPolicy UndecidedPolicy { get; }

    /// <summary>
    ///     Finds the translator registration for one domain event type.
    /// </summary>
    /// <param name="eventType">The runtime type of the drained domain event.</param>
    /// <param name="registration">When this method returns <see langword="true" />, the translator registration.</param>
    /// <returns><see langword="true" /> when the event type has a translator; otherwise, <see langword="false" />.</returns>
    public bool TryGetTranslator(Type eventType, [NotNullWhen(true)] out DomainEventTranslatorRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return _translators.TryGetValue(eventType, out registration);
    }

    /// <summary>
    ///     Tests whether one domain event type is dropped without staging.
    /// </summary>
    /// <param name="eventType">The runtime type of the drained domain event.</param>
    /// <returns><see langword="true" /> when the event type is ignored; otherwise, <see langword="false" />.</returns>
    public bool IsIgnored(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return _ignored.Contains(eventType);
    }

    /// <summary>
    ///     Tests whether one domain event type is published in process after the commit.
    /// </summary>
    /// <param name="eventType">The runtime type of the drained domain event.</param>
    /// <returns><see langword="true" /> when the event type publishes in process; otherwise, <see langword="false" />.</returns>
    public bool IsPublishedInProcess(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        return _publishedInProcess.Contains(eventType);
    }
}
