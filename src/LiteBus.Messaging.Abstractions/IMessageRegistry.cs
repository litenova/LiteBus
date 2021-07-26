using System;
using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageRegistry : IReadOnlyCollection<IMessageDescriptor>
    {
        void RegisterHandler(Type handlerType);

        void RegisterPreHandleHook(Type preHandleHookType);

        void RegisterPostHandleHook(Type postHandleHookType);
    }
}