using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Marks an outbox storage sub-module registered by the outbox core builder.
/// </summary>
public interface IOutboxStorageModule : IModule
{
}
