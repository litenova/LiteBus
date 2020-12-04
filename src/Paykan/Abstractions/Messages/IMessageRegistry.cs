using System;
using System.Collections.Generic;

namespace Paykan.Abstractions
{
    public interface IMessageRegistry : IReadOnlyDictionary<Type, IMessageDescriptor>
    {
        
    }
}