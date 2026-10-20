using System;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Summarizes failing outbox messages for one message contract.
/// </summary>
/// <remarks>
///     One row is returned per contract name and version that has at least one <see cref="OutboxStatus.Failed" /> or
///     <see cref="OutboxStatus.DeadLettered" /> message, so a health probe can tell messages that fail on every attempt
///     apart from a backlog that is merely slow without paging through individual rows.
/// </remarks>
public sealed record OutboxFailureSummary
{
    /// <summary>
    ///     Gets the stable message contract name shared by the failing messages.
    /// </summary>
    public required string ContractName { get; init; }

    /// <summary>
    ///     Gets the message contract version shared by the failing messages.
    /// </summary>
    public required int ContractVersion { get; init; }

    /// <summary>
    ///     Gets the number of messages in <see cref="OutboxStatus.Failed" /> that are awaiting retry.
    /// </summary>
    public required int FailedCount { get; init; }

    /// <summary>
    ///     Gets the number of messages in <see cref="OutboxStatus.DeadLettered" /> that need an operator replay.
    /// </summary>
    public required int DeadLetteredCount { get; init; }

    /// <summary>
    ///     Gets the error text of the most recently attempted failing message of this contract.
    /// </summary>
    /// <value>
    ///     The error text, or <see langword="null" /> when no failing message of this contract has error text.
    /// </value>
    public string? LastError { get; init; }

    /// <summary>
    ///     Gets the CLR type name of the exception behind <see cref="LastError" />.
    /// </summary>
    /// <value>
    ///     The type name, or <see langword="null" /> when the most recent failure carried no exception.
    /// </value>
    public string? LastErrorType { get; init; }

    /// <summary>
    ///     Gets the latest <see cref="OutboxEnvelope.LastAttemptedAt" /> among the failing messages of this contract.
    /// </summary>
    /// <value>
    ///     The timestamp, or <see langword="null" /> when none of the failing messages has been attempted.
    /// </value>
    public DateTimeOffset? LastFailedAt { get; init; }
}
