using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     An entity that buffers the domain events it raised until its unit of work commits.
/// </summary>
/// <remarks>
///     Storage harvesters read entities that implement this interface by default. An application whose aggregate base
///     type has its own shape configures accessors on the outbox builder instead of implementing it.
/// </remarks>
public interface IHasDomainEvents
{
    /// <summary>
    ///     Gets the domain events the entity raised and has not yet handed over.
    /// </summary>
    IReadOnlyCollection<object> DomainEvents { get; }

    /// <summary>
    ///     Removes every buffered domain event, after a harvester collected them.
    /// </summary>
    void ClearDomainEvents();
}
