using System;
using System.Collections.Generic;
using Paykan.Abstractions;

namespace Paykan.Internal
{
    internal class MessageDescriptor : IMessageDescriptor
    {
        private readonly List<Type> _handlerTypes = new List<Type>();
        private readonly List<Type> _postHandleHooks = new List<Type>();

        public MessageDescriptor(Type messageType)
        {
            MessageType = messageType;
        }

        public Type MessageType { get; }
        public IReadOnlyCollection<Type> HandlerTypes => _handlerTypes.AsReadOnly();
        public IReadOnlyCollection<Type> PostHandleHookTypes => _postHandleHooks.AsReadOnly();

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