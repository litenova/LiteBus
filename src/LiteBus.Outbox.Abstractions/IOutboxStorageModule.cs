using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Marker for outbox storage sub-modules registered through <see cref="OutboxModuleBuilder.RegisterStorage" />.
/// </summary>
public interface IOutboxStorageModule : IModule
{
}