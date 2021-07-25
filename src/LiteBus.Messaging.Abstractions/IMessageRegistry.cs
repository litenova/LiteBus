using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageRegistry : IEnumerable<IMessageDescriptor>
    {
        void RegisterHandler(Type type);

        void RegisterPreHandleHook(Type type);

        void RegisterPostHandleHook(Type type);
    }
}