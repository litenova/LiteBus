namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Represents the envelope and insertion outcome returned by an outbox append store.
/// </summary>
/// <param name="Envelope">The envelope that remains the stored source of truth.</param>
/// <param name="Outcome">Whether this append inserted the envelope or resolved an existing envelope.</param>
public sealed record OutboxAppendResult(OutboxEnvelope Envelope, OutboxEnqueueOutcome Outcome);
