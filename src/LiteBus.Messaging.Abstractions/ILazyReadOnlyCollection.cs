using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions
{
    public interface ILazyReadOnlyCollection<T> : IReadOnlyCollection<Lazy<T>>
    {
        
    }
}