using System;
using System.Collections.Generic;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     Reads and clears the domain events of one aggregate type with caller-supplied accessors.
/// </summary>
/// <param name="AggregateType">The aggregate type the accessors read.</param>
/// <param name="ReadEvents">Reads the buffered domain events of one aggregate instance.</param>
/// <param name="ClearEvents">Removes the buffered domain events of one aggregate instance after harvesting.</param>
internal sealed record DomainEventAccessor(Type AggregateType, Func<object, IEnumerable<object>> ReadEvents, Action<object> ClearEvents)
{
    /// <summary>
    ///     Creates an accessor that casts the untyped entity to the aggregate type before delegating.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate type the accessors read.</typeparam>
    /// <param name="events">Reads the buffered domain events of one aggregate instance.</param>
    /// <param name="clear">Removes the buffered domain events of one aggregate instance after harvesting.</param>
    /// <returns>The untyped accessor stored on the registry.</returns>
    internal static DomainEventAccessor Create<TAggregate>(Func<TAggregate, IEnumerable<object>> events, Action<TAggregate> clear)
        where TAggregate : class
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clear);

        return new DomainEventAccessor(
            typeof(TAggregate),
            aggregate => events((TAggregate) aggregate),
            aggregate => clear((TAggregate) aggregate));
    }
}
