using System;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class HandlerDescriptor : IHandlerDescriptor
    {
        public Type HandlerType { get; set; }

        public Type MessageType { get; set; }

        public Type MessageResultType { get; set; }

        public bool IsGeneric { get; set; }
    }
}