using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     Maps a mediation outcome to an audit outcome without any application knowledge.
/// </summary>
/// <remarks>
///     The pipeline already separates a refusal from an early answer, so the mapping needs no guesswork: a guard denial
///     is a denial, and a shortcut answer is a success because nothing was refused. Applications that refuse by throwing
///     register their own <see cref="IAuditOutcomeMapper" /> so that their refusal exception is recorded as
///     <see cref="AuditOutcome.Denied" /> rather than <see cref="AuditOutcome.Failed" />.
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
            MessageOutcome.Answered => AuditOutcome.Succeeded,
            MessageOutcome.Denied => AuditOutcome.Denied,
            MessageOutcome.Canceled => AuditOutcome.Canceled,
            MessageOutcome.Invalid => AuditOutcome.Invalid,
            _ => AuditOutcome.Failed
        };
    }
}
