using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Describes one outbox delivery: which envelope, which attempt, which cause and identity the reaction runs
///     with, and which delivery path started it.
/// </summary>
/// <param name="EnvelopeId">The identifier of the envelope being dispatched.</param>
/// <param name="ContractName">The stable contract name of the envelope being dispatched.</param>
/// <param name="AttemptNumber">The leased envelope attempt count for this dispatch.</param>
/// <param name="Cause">The committed fact the reaction runs with, identical to the mediation cause.</param>
/// <param name="Identity">The identity the reaction runs as.</param>
/// <param name="Kind">The delivery path that started this dispatch.</param>
/// <remarks>
///     <para>
///         The dispatcher stamps one instance into the mediation items under <see cref="MediationItemsKey" /> before
///         publishing, so handlers and mediation observers read the delivery without ambient state: the attempt
///         number answers "how many times has the outbox tried this envelope", the cause answers "which committed
///         fact started this reaction", and the kind answers "did this run right after the commit or later from the
///         processor". Retry policies receive the same instance on
///         <see cref="OutboxReactionRetryContext.Dispatch" />.
///     </para>
///     <para>
///         Each delivery runs as a root mediation: the reaction carries <see cref="Cause" /> as data and never
///         inherits the ambient scope of the mediation that staged the envelope.
///     </para>
/// </remarks>
public sealed record OutboxDispatchContext(
    Guid EnvelopeId,
    string ContractName,
    int AttemptNumber,
    MediationCause Cause,
    ExecutionIdentity Identity,
    OutboxDispatchKind Kind)
{
    /// <summary>
    ///     Gets the mediation items key the dispatcher stamps the dispatch context under.
    /// </summary>
    public const string MediationItemsKey = "__LiteBus.Outbox.Dispatch";

    /// <summary>
    ///     Reads the dispatch context a dispatcher stamped into mediation items.
    /// </summary>
    /// <param name="items">The mediation or execution items to read, or <see langword="null" /> for none.</param>
    /// <param name="dispatch">
    ///     The stamped dispatch context when this method returns <see langword="true" />; otherwise,
    ///     <see langword="null" />.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> when <paramref name="items" /> carries an outbox dispatch context; otherwise,
    ///     <see langword="false" />, which also covers a mediation the outbox did not start.
    /// </returns>
    public static bool TryGet(
        IDictionary<string, object>? items,
        [NotNullWhen(true)] out OutboxDispatchContext? dispatch)
    {
        if (items is not null
            && items.TryGetValue(MediationItemsKey, out var stored)
            && stored is OutboxDispatchContext context)
        {
            dispatch = context;
            return true;
        }

        dispatch = null;
        return false;
    }
}
