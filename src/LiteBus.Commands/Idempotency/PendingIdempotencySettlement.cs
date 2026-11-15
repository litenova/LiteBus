using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Idempotency;

/// <summary>
///     An idempotency claim whose settlement waits for the unit-of-work commit.
/// </summary>
/// <param name="Claimed">The key the mediation was granted, carrying the token that settles it.</param>
/// <param name="Outcome">How the mediation ended when the settlement was deferred.</param>
/// <param name="Payload">The serialized result recorded for replay, or <see langword="null" /> to record none.</param>
/// <remarks>
///     <para>
///         Recorded in the execution-context data by <see cref="IdempotencyCompletionHandler" /> when the store commits
///         its own writes (see <see cref="ISeparatelyCommittedIdempotencyStore" />) and a unit of work is registered:
///         completing the key before the commit would leave it marked complete with no data behind it when the commit
///         throws, and the caller's retry would be answered as a repeat. The command unit-of-work handler reads this
///         record back after <see cref="IUnitOfWork.CommitAsync" /> returns and completes the key then, or releases it
///         when the commit throws.
///     </para>
///     <para>
///         Inside a command batch the same record is added to the <see cref="CommandBatchIdempotencyParticipant" />
///         for every store, marked or not, because the batch settles all of its commands' claims at once after the
///         outcome is known.
///     </para>
///     <para>
///         One value per mediation is enough: a mediation claims at most one key, so a single command defers at most
///         one settlement, and a batch holds one per command that was granted a claim.
///     </para>
/// </remarks>
internal sealed record PendingIdempotencySettlement(ClaimedIdempotencyKey Claimed, MediationOutcome Outcome, string? Payload);
