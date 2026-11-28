using System;
using System.Collections;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     Verifies every event the outbox stages has a registered message contract and a subscribed handler.
/// </summary>
/// <remarks>
///     <para>
///         A staged event with no handler and no declared ignore never delivers: the outbox publishes it and no
///         reaction runs. The check reads the staged decisions from the <c>outbox-domain-events</c> composition
///         section and requires each staged event type to have a contract in
///         <see cref="ICompositionModel.Contracts" /> and a main handler in
///         <see cref="ICompositionModel.Handlers" />. Ignored, translated, and in-process decisions need no handler.
///     </para>
///     <para>
///         The section is read by name through <see cref="ICompositionSectionContributor.GetData" /> so this host
///         adapter stays decoupled from the outbox package. Events staged by explicit enqueue calls are runtime
///         behavior and are out of reach of a composition check by design.
///     </para>
/// </remarks>
internal static class DurableEventConsistencyCheck
{
    /// <summary>
    ///     The composition section reporting the per-type domain event decisions.
    /// </summary>
    private const string DecisionsSectionName = "outbox-domain-events";

    /// <summary>
    ///     The decision kind staging an event unchanged.
    /// </summary>
    private const string StageDecisionKind = "Stage";

    /// <summary>
    ///     The handler stage running the subscribed main handler of an event.
    /// </summary>
    private const string MainHandlerStage = "handler";

    /// <summary>
    ///     Verifies every staged event has a contract and a handler, recording one failure per problem found.
    /// </summary>
    /// <param name="services">The root service provider built by the host.</param>
    /// <param name="failures">The failure messages collected so far.</param>
    /// <remarks>
    ///     Skipped silently when messaging was not composed. A host with no outbox decisions section stages nothing
    ///     through the domain-event collector and passes.
    /// </remarks>
    internal static void Validate(IServiceProvider services, List<string> failures)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(failures);

        if (services.GetService(typeof(ICompositionModel)) is not ICompositionModel model)
        {
            return;
        }

        foreach (var stagedEvent in ReadStagedEvents(model, failures))
        {
            if (!HasContract(model, stagedEvent))
            {
                failures.Add(
                    $"event consistency for '{stagedEvent}': the outbox stages this event but no message contract "
                    + "is registered for it. Register a contract or stop staging it.");
            }

            if (!HasMainHandler(model, stagedEvent))
            {
                failures.Add(
                    $"event consistency for '{stagedEvent}': the outbox stages this event but no handler "
                    + "subscribes to it. Register a handler or declare the event ignored.");
            }
        }
    }

    /// <summary>
    ///     Reads the event type names the outbox stages unchanged.
    /// </summary>
    /// <param name="model">The composition model.</param>
    /// <param name="failures">The failure messages collected so far.</param>
    /// <returns>The staged event type full names, in section order.</returns>
    /// <remarks>
    ///     A decisions section with an unrecognized shape fails the check naming the section: the check cannot
    ///     verify staged events it cannot read, and skipping them silently would hide the gap.
    /// </remarks>
    private static List<string> ReadStagedEvents(ICompositionModel model, List<string> failures)
    {
        List<string> staged = [];

        foreach (var section in model.Sections)
        {
            if (!string.Equals(section.SectionName, DecisionsSectionName, StringComparison.Ordinal))
            {
                continue;
            }

            if (section.GetData() is not IDictionary data
                || data["decisions"] is not IEnumerable decisions)
            {
                failures.Add(
                    $"event consistency: the '{DecisionsSectionName}' composition section has an unrecognized "
                    + "shape, so staged events cannot be verified.");
                continue;
            }

            foreach (var decision in decisions)
            {
                if (decision is IDictionary entry
                    && entry["eventType"] is string eventType
                    && entry["kind"] is string kind
                    && string.Equals(kind, StageDecisionKind, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(eventType))
                {
                    staged.Add(eventType);
                }
            }
        }

        return staged;
    }

    /// <summary>
    ///     Determines whether a message contract is registered for one event type name.
    /// </summary>
    /// <param name="model">The composition model.</param>
    /// <param name="eventTypeName">The event type full name.</param>
    /// <returns><see langword="true" /> when a contract names the event type.</returns>
    private static bool HasContract(ICompositionModel model, string eventTypeName)
    {
        foreach (var contract in model.Contracts)
        {
            if (string.Equals(contract.MessageType.FullName, eventTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether a main handler subscribes to one event type name.
    /// </summary>
    /// <param name="model">The composition model.</param>
    /// <param name="eventTypeName">The event type full name.</param>
    /// <returns>
    ///     <see langword="true" /> when a main handler entry names the event type, directly or through a base type
    ///     or interface the handler is registered for.
    /// </returns>
    private static bool HasMainHandler(ICompositionModel model, string eventTypeName)
    {
        foreach (var handler in model.Handlers)
        {
            if (string.Equals(handler.Stage, MainHandlerStage, StringComparison.Ordinal)
                && string.Equals(handler.MessageType.FullName, eventTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
