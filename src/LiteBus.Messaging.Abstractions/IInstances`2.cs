using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

public interface IInstances<out TDescriptor> : IReadOnlyCollection<IInstance<TDescriptor>>
{
}