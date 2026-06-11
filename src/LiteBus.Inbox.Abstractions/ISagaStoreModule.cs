using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Marker for saga storage sub-modules registered through <see cref="InboxModuleBuilder.RegisterSaga" />.
/// </summary>
public interface ISagaStoreModule : IModule
{
}