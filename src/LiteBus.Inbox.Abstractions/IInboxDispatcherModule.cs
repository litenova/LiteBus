using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Marker for inbox dispatcher sub-modules registered through <see cref="InboxModuleBuilder.RegisterDispatcher" />.
/// </summary>
public interface IInboxDispatcherModule : IModule
{
}
