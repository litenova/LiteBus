using System;
using System.Collections.Generic;
using JasperFx;
using JasperFx.Events;
using LiteBus.Messaging.Abstractions;
using Marten.Exceptions;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Turns a Marten optimistic concurrency or event-stream version conflict into a denial refusal.
/// </summary>
/// <remarks>
///     <para>
///         A document or event stream changed by another writer after the handler read it fails the unit-of-work
///         commit, long after the guards ran, so without a classifier the loser sees a storage exception every
///         application maps by hand at its boundary. Registered through
///         <c>MessageModuleBuilder.UseFaultRefusalClassifierInstance</c>, this classifier ends the mediation as a
///         <see cref="Refusal" /> denial carrying <see cref="ConcurrencyConflictCode" /> instead, so a
///         <c>TrySendAsync</c> call reports it as a value.
///     </para>
///     <para>
///         The conflict is a denial rather than invalid input: the message was well-formed, but the state it was
///         computed from is no longer current. The caller reloads and retries. Only concurrency failures are
///         recognized: a <see cref="ConcurrencyException" />, which covers the event-stream version conflict
///         <see cref="EventStreamUnexpectedMaxEventIdException" /> through inheritance, and a
///         <see cref="ConcurrentUpdateException" />, which is how Marten surfaces a serialization conflict the
///         application must resolve by re-reading. Lock, usage, and constraint faults pass through untouched.
///     </para>
/// </remarks>
public sealed class MartenConcurrencyRefusalClassifier : IFaultRefusalClassifier
{
    /// <summary>
    ///     The refusal code reported for a Marten concurrency conflict.
    /// </summary>
    public const string ConcurrencyConflictCode = ConcurrencyRefusalCodes.Conflict;

    /// <inheritdoc />
    public bool TryClassify(Exception exception, out Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(exception);

        refusal = default;

        // A commit fault usually arrives wrapped: the store, the session, or the unit of work adds its own layer
        // around the Marten exception, and a batched commit can aggregate several, so every reachable exception
        // is checked rather than just the top one.
        foreach (var current in EnumerateExceptions(exception))
        {
            if (current is EventStreamUnexpectedMaxEventIdException)
            {
                refusal = Refusal.Denied(
                    "The event stream was changed by another writer; reload the stream and retry the command.",
                    ConcurrencyConflictCode);
                return true;
            }

            if (current is ConcurrencyException or ConcurrentUpdateException)
            {
                refusal = Refusal.Denied(
                    "The document was changed by another writer; reload it and retry the command.",
                    ConcurrencyConflictCode);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Enumerates an exception, its inner exceptions, and every exception an <see cref="AggregateException" />
    ///     carries, depth first.
    /// </summary>
    /// <param name="exception">The exception to start from.</param>
    /// <returns>Every exception reachable from <paramref name="exception" />.</returns>
    /// <remarks>
    ///     A batched commit can report several failures in one <see cref="AggregateException" />, and its
    ///     <see cref="Exception.InnerException" /> is only the first of them, so walking that chain alone can miss
    ///     the storage error that matters.
    /// </remarks>
    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            if (current is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
    }
}
