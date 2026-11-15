using System;
using System.Collections.Generic;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.ImmediateDispatch;

/// <summary>
///     Collects the identifiers of the immediate envelopes staged in one mediation scope, so the commit listener can
///     dispatch exactly those after the unit of work commits.
/// </summary>
/// <remarks>
///     Registered as a scoped service by <see cref="OutboxModuleBuilder.EnableImmediateDispatch" />. Every shipped
///     transactional outbox path adds the envelopes it stages. A command batch shares one scope across its commands, so
///     one instance holds every command's envelopes. Access is synchronized because a handler may stage from parallel
///     work inside one mediation.
/// </remarks>
internal sealed class StagedImmediateEnvelopes
{
    /// <summary>
    ///     Guards <see cref="_messageIds" /> and <see cref="_seen" />.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The staged identifiers, in staging order.
    /// </summary>
    private readonly List<Guid> _messageIds = [];

    /// <summary>
    ///     The staged identifiers, for de-duplication of a retried enqueue that resolved to the same envelope.
    /// </summary>
    private readonly HashSet<Guid> _seen = [];

    /// <summary>
    ///     Gets a value indicating whether nothing is staged.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _messageIds.Count == 0;
            }
        }
    }

    /// <summary>
    ///     Records a staged immediate envelope.
    /// </summary>
    /// <param name="messageId">The envelope identifier.</param>
    public void Add(Guid messageId)
    {
        lock (_gate)
        {
            if (_seen.Add(messageId))
            {
                _messageIds.Add(messageId);
            }
        }
    }

    /// <summary>
    ///     Returns the staged identifiers in staging order and clears the collection, so one commit dispatches each
    ///     envelope once.
    /// </summary>
    /// <returns>The identifiers staged since the last call.</returns>
    public IReadOnlyList<Guid> Drain()
    {
        lock (_gate)
        {
            var drained = _messageIds.ToArray();
            _messageIds.Clear();
            _seen.Clear();
            return drained;
        }
    }
}
