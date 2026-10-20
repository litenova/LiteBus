using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes one failed attempt at handling or dispatching a durable message.
/// </summary>
/// <remarks>
///     <para>
///         The three parts are separated because an operator asks three different questions of them. The formatted
///         <see cref="Error" /> is what a person reads, <see cref="ErrorType" /> is what a dashboard groups and filters
///         by, and <see cref="OccurredAt" /> is what answers when a message started failing and when it gave up.
///         Storing only the formatted string forces every consumer to parse it back apart.
///     </para>
///     <para>
///         Processors build this from the exception and hand it to the envelope transition, so the store persists what
///         the processor decided rather than deriving failure metadata of its own.
///     </para>
/// </remarks>
public sealed record MessageFailure
{
    /// <summary>
    ///     Gets the formatted, length-capped error text persisted for an operator to read.
    /// </summary>
    public required string Error { get; init; }

    /// <summary>
    ///     Gets the full CLR type name of the exception that caused the failure.
    /// </summary>
    /// <value>
    ///     The type name, or <see langword="null" /> when the failure did not come from an exception. It is stored
    ///     separately from <see cref="Error" /> so a dead-letter listing can group by failure kind without parsing
    ///     message text.
    /// </value>
    public string? ErrorType { get; init; }

    /// <summary>
    ///     Gets the UTC timestamp at which the attempt failed.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    ///     Creates a failure from the exception that ended an attempt.
    /// </summary>
    /// <param name="exception">The exception the attempt threw.</param>
    /// <param name="occurredAt">The UTC timestamp at which the attempt failed.</param>
    /// <returns>The failure to hand to an envelope transition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static MessageFailure From(Exception exception, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new MessageFailure
        {
            Error = MessageProcessorDiagnostics.FormatError(exception),
            ErrorType = exception.GetType().FullName ?? exception.GetType().Name,
            OccurredAt = occurredAt
        };
    }

    /// <summary>
    ///     Creates a failure from already formatted error text.
    /// </summary>
    /// <param name="error">The formatted error text.</param>
    /// <param name="occurredAt">The UTC timestamp at which the attempt failed.</param>
    /// <returns>The failure to hand to an envelope transition.</returns>
    /// <exception cref="ArgumentException"><paramref name="error" /> is null, empty, or white space.</exception>
    /// <remarks>
    ///     For callers that have a reason rather than an exception, such as a requeue refusal or a hook policy. The
    ///     resulting failure carries no <see cref="ErrorType" />, which is the honest answer when no exception exists.
    /// </remarks>
    public static MessageFailure FromError(string error, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return new MessageFailure
        {
            Error = error,
            OccurredAt = occurredAt
        };
    }
}
