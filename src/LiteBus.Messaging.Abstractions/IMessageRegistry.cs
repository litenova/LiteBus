using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageRegistry : IReadOnlyCollection<IMessageDescriptor>
    {
        void RegisterHandler(Type handlerType);

        void RegisterPreHandler(Type preHandlerType);

        void RegisterPostHandler(Type postHandlerType);
    }
}