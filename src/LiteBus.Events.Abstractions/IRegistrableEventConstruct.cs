namespace LiteBus.Events.Abstractions;

/// <summary>
///     Marker interface that identifies types that can be registered within the event module.
/// </summary>
/// <remarks>
///     This interface is implemented by all key constructs in the event module that need to be
///     registered in the dependency injection container, such as events, event handlers,
///     pre-handlers, post-handlers, and error handlers. It provides a common type for registration
///     and discovery mechanisms in the LiteBus infrastructure.
/// </remarks>
public interface IRegistrableEventConstruct;