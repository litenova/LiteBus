using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LiteBus.Messaging.Abstractions
{
    public interface ILazyReadOnlyCollection<T> : IReadOnlyCollection<Lazy<T>>
    {
        
    }
}