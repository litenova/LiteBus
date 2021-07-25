using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Registry
{
    internal class MessageDescriptor : IMessageDescriptor
    {
        private readonly HashSet<Type> _handlerTypes = new();
        private readonly HashSet<Type> _postHandleHooks = new();
        private readonly HashSet<Type> _preHandleHooks = new();

        public MessageDescriptor(Type messageType)
        {
            MessageType = messageType;
        }

        public Type MessageType { get; }

        public IReadOnlyCollection<Type> HandlerTypes => _handlerTypes;

        public IReadOnlyCollection<Type> PostHandleHookTypes => _postHandleHooks;

        public IReadOnlyCollection<Type> PreHandleHookTypes => _preHandleHooks;

        public void AddHandlerType(Type type)
        {
            _handlerTypes.Add(type);
        }

        public void AddPostHandleHookType(Type type)
        {
            _postHandleHooks.Add(type);
        }
        
        public void AddPreHandleHookType(Type type)
        {
            _preHandleHooks.Add(type);
        }
    }
}