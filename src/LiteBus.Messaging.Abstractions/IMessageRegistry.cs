using System;
using System.Collections.Generic;
using System.Reflection;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageRegistry : IReadOnlyCollection<IMessageDescriptor>
    {
        void RegisterHandler(Type handlerType);

        void RegisterPreHandleHook(Type preHandleHookType);

        void RegisterPostHandleHook(Type postHandleHookType);
    }
}