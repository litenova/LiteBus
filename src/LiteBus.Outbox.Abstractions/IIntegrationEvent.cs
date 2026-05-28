namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Marks an event as intended for publication outside the current bounded context.
/// </summary>
/// <remarks>
///     <para>
///         Integration events are public facts that another boundary can consume later. Keep them stable, serializable,
///         and versioned through durable message contracts. Do not reuse a mutable domain event type as an integration
///         event unless that type is already safe to store and publish as an external contract.
///     </para>
/// </remarks>
public interface IIntegrationEvent;