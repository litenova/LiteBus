using System;
using System.Collections.Generic;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     Reads and clears the domain events of the entities one registration selects, with caller-supplied accessors.
/// </summary>
/// <param name="AggregateType">The aggregate type the accessors read, or <see langword="null" /> when a predicate selects the entities.</param>
/// <param name="IsMatch">Tests whether the accessors read one tracked entity type.</param>
/// <param name="ReadEvents">Reads the buffered domain events of one aggregate instance.</param>
/// <param name="ClearEvents">Removes the buffered domain events of one aggregate instance after harvesting.</param>
internal sealed record DomainEventAccessor(
    Type? AggregateType,
    Func<Type, bool> IsMatch,
    Func<object, IEnumerable<object>> ReadEvents,
    Action<object> ClearEvents)
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

        return Create(
            typeof(TAggregate),
            aggregate => events((TAggregate) aggregate),
            aggregate => clear((TAggregate) aggregate));
    }

    /// <summary>
    ///     Creates an accessor for one aggregate type given as a <see cref="Type" />, including an open generic definition.
    /// </summary>
    /// <param name="aggregateType">The aggregate type the accessors read.</param>
    /// <param name="events">Reads the buffered domain events of one aggregate instance.</param>
    /// <param name="clear">Removes the buffered domain events of one aggregate instance after harvesting.</param>
    /// <returns>The untyped accessor stored on the registry.</returns>
    internal static DomainEventAccessor Create(
        Type aggregateType,
        Func<object, IEnumerable<object>> events,
        Action<object> clear)
    {
        ArgumentNullException.ThrowIfNull(aggregateType);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clear);

        return new DomainEventAccessor(
            aggregateType,
            entityType => IsTypeMatch(aggregateType, entityType),
            events,
            clear);
    }

    /// <summary>
    ///     Creates an accessor for the entity types one predicate selects, for aggregates with no common base.
    /// </summary>
    /// <param name="predicate">Tests whether the accessors read one tracked entity type.</param>
    /// <param name="events">Reads the buffered domain events of one aggregate instance.</param>
    /// <param name="clear">Removes the buffered domain events of one aggregate instance after harvesting.</param>
    /// <returns>The untyped accessor stored on the registry.</returns>
    internal static DomainEventAccessor CreateWhere(
        Func<Type, bool> predicate,
        Func<object, IEnumerable<object>> events,
        Action<object> clear)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clear);

        return new DomainEventAccessor(null, predicate, events, clear);
    }

    /// <summary>
    ///     Tests whether one tracked entity type is read through one registered aggregate type.
    /// </summary>
    /// <param name="aggregateType">The registered aggregate type.</param>
    /// <param name="entityType">The runtime type of the tracked entity.</param>
    /// <returns><see langword="true" /> when the entity type matches the registered type; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     A closed type matches the entity type itself and anything derived from or implementing it, so proxies and
    ///     subclasses harvest through the configured aggregate. An open generic definition matches when it appears in
    ///     the generic definitions up the entity's base-type chain or among its implemented interfaces, so one
    ///     registration covers every closed aggregate built on the same base.
    /// </remarks>
    private static bool IsTypeMatch(Type aggregateType, Type entityType)
    {
        if (aggregateType == entityType || aggregateType.IsAssignableFrom(entityType))
        {
            return true;
        }

        if (!aggregateType.IsGenericTypeDefinition)
        {
            return false;
        }

        for (var current = entityType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == aggregateType)
            {
                return true;
            }
        }

        foreach (var implemented in entityType.GetInterfaces())
        {
            if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == aggregateType)
            {
                return true;
            }
        }

        return false;
    }
}
