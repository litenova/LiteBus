using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Marker for outbox dispatcher sub-modules registered through <see cref="OutboxModuleBuilder.RegisterDispatcher" />.
/// </summary>
public interface IOutboxDispatcherModule : IModule
{
}
