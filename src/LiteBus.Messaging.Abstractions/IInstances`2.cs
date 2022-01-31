using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

public interface IInstances<out TInstance, out TDescriptor> : IReadOnlyCollection<IInstance<TInstance, TDescriptor>>
{
}