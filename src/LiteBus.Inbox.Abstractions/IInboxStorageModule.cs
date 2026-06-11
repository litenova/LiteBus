using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Marker for inbox storage sub-modules registered through <see cref="InboxModuleBuilder.RegisterStorage" />.
/// </summary>
public interface IInboxStorageModule : IModule
{
}