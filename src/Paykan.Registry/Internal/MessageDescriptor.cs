using System;
using System.Collections.Generic;
using Paykan.Registry.Abstractions;

namespace Paykan.Registry.Internal
{
    internal class MessageDescriptor : IMessageDescriptor
    {
        private readonly HashSet<Type> _handlerTypes = new();

        private readonly HashSet<Type> _postHandleHooks = new();

        public MessageDescriptor(Type messageType)
        {
            MessageType = messageType;
        }

        public Type MessageType { get; }

        public IReadOnlyCollection<Type> HandlerTypes => _handlerTypes;

        public IReadOnlyCollection<Type> PostHandleHookTypes => _postHandleHooks;

        public void AddHandlerType(Type type)
        {
            _handlerTypes.Add(type);
        }

        public void AddPostHandleHookType(Type type)
        {
            _postHandleHooks.Add(type);
        }
    }
}