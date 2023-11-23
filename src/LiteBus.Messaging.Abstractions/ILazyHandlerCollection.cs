using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

public interface ILazyHandlerCollection<THandler, TDescriptor> : IReadOnlyCollection<LazyHandler<THandler, TDescriptor>>
{
}