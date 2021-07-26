using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class MessageDescriptor : IMessageDescriptor
    {
        private readonly HashSet<Type> _handlerTypes = new();
        private readonly HashSet<PostHandleHookDescriptor> _postHandleHooks = new();
        private readonly HashSet<PreHandleHookDescriptor> _preHandleHooks = new();

        public MessageDescriptor(Type messageType)
        {
            MessageType = messageType;
        }

        public Type MessageType { get; }

        public IReadOnlyCollection<Type> HandlerTypes => _handlerTypes;

        public IReadOnlyCollection<IHookDescriptor> PostHandleHookDescriptors => _postHandleHooks;

        public IReadOnlyCollection<IHookDescriptor> PreHandleHookDescriptors => _preHandleHooks;

        public void AddHandlerType(Type type)
        {
            _handlerTypes.Add(type);
        }

        public void AddPostHandleHookDescriptor(PostHandleHookDescriptor preHandleHookDescriptor)
        {
            _postHandleHooks.Add(preHandleHookDescriptor);
        }

        public void AddPreHandleHookDescriptor(PreHandleHookDescriptor preHandleHookDescriptor)
        {
            _preHandleHooks.Add(preHandleHookDescriptor);
        }
    }
}