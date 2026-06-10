using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Marker for inbox ingress sub-modules registered through <see cref="InboxModuleBuilder.RegisterIngress" />.
/// </summary>
public interface IInboxIngressModule : IModule
{
}
