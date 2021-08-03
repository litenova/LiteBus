using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class MessageDescriptor : IMessageDescriptor
    {
        private readonly HashSet<HandlerDescriptor> _handlers = new();
        private readonly HashSet<PostHandleHookDescriptor> _postHandleHooks = new();
        private readonly HashSet<PreHandleHookDescriptor> _preHandleHooks = new();


        public MessageDescriptor(Type messageType)
        {
            MessageType = messageType;
        }

        public Type MessageType { get; }

        public IReadOnlyCollection<IHandlerDescriptor> HandlerDescriptors => _handlers;

        public IReadOnlyCollection<IHookDescriptor> PostHandleHookDescriptors => _postHandleHooks;

        public IReadOnlyCollection<IHookDescriptor> PreHandleHookDescriptors => _preHandleHooks;


        public void AddHandlerDescriptor(HandlerDescriptor type)
        {
            _handlers.Add(type);
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