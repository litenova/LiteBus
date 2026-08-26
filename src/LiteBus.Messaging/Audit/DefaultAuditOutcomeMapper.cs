using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     Maps a mediation outcome to an audit outcome without any application knowledge.
/// </summary>
/// <remarks>
///     An aborted mediation is recorded as a denial, because aborting is how a pre-handler refuses to let a message
///     proceed. Applications that refuse by throwing should register their own <see cref="IAuditOutcomeMapper" /> so
///     that their refusal exception is recorded as <see cref="AuditOutcome.Denied" /> rather than
///     <see cref="AuditOutcome.Failed" />.
/// </remarks>
public sealed class DefaultAuditOutcomeMapper : IAuditOutcomeMapper
{
    /// <inheritdoc />
    public AuditOutcome Map(MessageCompletionContext context)
    {
        return MapByOutcome(context);
    }

    /// <summary>
    ///     Maps a completion context using its <see cref="MessageCompletionContext.Outcome" /> alone.
    /// </summary>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <returns>The audit outcome for the mediation outcome.</returns>
    /// <remarks>
    ///     Exposed so a custom mapper can delegate the cases it does not want to special-case.
    /// </remarks>
    public static AuditOutcome MapByOutcome(MessageCompletionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Outcome switch
        {
            MessageOutcome.Succeeded => AuditOutcome.Succeeded,
            MessageOutcome.Aborted => AuditOutcome.Denied,
            MessageOutcome.Canceled => AuditOutcome.Canceled,
            _ => AuditOutcome.Failed
        };
    }
}
