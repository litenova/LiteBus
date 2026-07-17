using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Marks an inbox ingress sub-module registered by the inbox core builder.
/// </summary>
public interface IInboxIngressModule : IModule
{
}
