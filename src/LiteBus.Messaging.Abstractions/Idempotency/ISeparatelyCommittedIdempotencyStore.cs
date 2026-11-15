namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks an idempotency store that commits its own writes, separately from the application's unit of work.
/// </summary>
/// <remarks>
///     <para>
///         Implement this marker when <see cref="IIdempotencyStore.CompleteAsync" /> and
///         <see cref="IIdempotencyStore.ReleaseAsync" /> open their own connection, session or transaction and commit it
///         themselves. The shipped PostgreSQL and Marten stores do.
///     </para>
///     <para>
///         When a unit of work is registered, LiteBus settles a marked store's claim only after
///         <see cref="IUnitOfWork.CommitAsync" /> returns, and releases the claim when the commit throws. Settling first
///         would leave the key marked complete with no data behind it after a failed commit, and the caller's retry would
///         be answered as a repeat. A post-commit settlement that keeps failing is logged and does not fail the
///         mediation, because the data is already committed; the key then stays claimed until the store's claim timeout.
///     </para>
///     <para>
///         Do not implement it for a store that writes through the application's unit of work. Such a store must settle
///         before the commit, inside the transaction, which is what LiteBus does for every store without the marker.
///     </para>
/// </remarks>
public interface ISeparatelyCommittedIdempotencyStore : IIdempotencyStore;
