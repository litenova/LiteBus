using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class MessageDescriptor : IMessageDescriptor
    {
        private readonly List<HandlerDescriptor> _handlers = new();
        private readonly List<PostHandlerDescriptor> _postHandlers = new();
        private readonly List<PreHandlerDescriptor> _preHandlers = new();

        public MessageDescriptor(Type messageType)
        {
            MessageType = messageType;
        }

        public Type MessageType { get; }

        public IReadOnlyCollection<IHandlerDescriptor> Handlers => _handlers;

        public IReadOnlyCollection<IPostHandlerDescriptor> PostHandlers => _postHandlers;

        public IReadOnlyCollection<IPreHandlerDescriptor> PreHandlers => _preHandlers;

        public void AddHandlerDescriptor(HandlerDescriptor handlerDescriptor)
        {
            if (_handlers.Any(h => h.HandlerType == handlerDescriptor.HandlerType))
            {
                return;
            }
            
            _handlers.Add(handlerDescriptor);
        }

        public void AddPostHandler(PostHandlerDescriptor postHandlerDescriptor)
        {
            if (_postHandlers.Any(h => h.PostHandlerType == postHandlerDescriptor.PostHandlerType))
            {
                return;
            }
            
            _postHandlers.Add(postHandlerDescriptor);
        }

        public void AddPreHandler(PreHandlerDescriptor preHandlerDescriptor)
        {
            if (_preHandlers.Any(h => h.PreHandlerType == preHandlerDescriptor.PreHandlerType))
            {
                return;
            }
            
            _preHandlers.Add(preHandlerDescriptor);
        }
    }
}