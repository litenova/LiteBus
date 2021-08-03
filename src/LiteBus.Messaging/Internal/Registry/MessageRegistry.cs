using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class MessageRegistry : IMessageRegistry
    {
        private readonly Dictionary<Type, MessageDescriptor> _descriptors = new();
        private readonly List<PostHandleHookDescriptor> _postHandlerHooks = new();
        private readonly List<PreHandleHookDescriptor> _preHandlerHooks = new();

        private event EventHandler<MessageDescriptor> NewMessageDescriptorCreated;

        public MessageRegistry()
        {
            NewMessageDescriptorCreated += UpdateNewMessagePostHandleHooks;
            NewMessageDescriptorCreated += UpdateNewMessagePreHandleHooks;
        }

        public IEnumerator<IMessageDescriptor> GetEnumerator() => _descriptors.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void RegisterHandler(Type handlerType)
        {
            foreach (var @interface in handlerType.GetInterfaces())
            {
                if (@interface.IsGenericType &&
                    @interface.GetGenericTypeDefinition().IsAssignableTo(typeof(IMessageHandler<,>)))
                {
                    var handlerDescriptor = new HandlerDescriptor
                    {
                        HandlerType = handlerType,
                        MessageType = @interface.GetGenericArguments()[0],
                        MessageResultType = @interface.GetGenericArguments()[1]
                    };
                    
                    var messageDescriptor = GetOrAddMessageDescriptor(handlerDescriptor.MessageType);

                    messageDescriptor.AddHandlerDescriptor(handlerDescriptor);
                }
            }
        }

        public void RegisterPreHandleHook(Type preHandleHookType)
        {
            foreach (var @interface in preHandleHookType.GetInterfaces())
            {
                if (@interface.IsGenericType &&
                    @interface.GetGenericTypeDefinition().IsAssignableTo(typeof(IPreHandleHook<>)))
                {
                    var messageType = @interface.GetGenericArguments()[0];

                    var hookDescriptor = new PreHandleHookDescriptor(preHandleHookType, messageType);

                    foreach (var messageDescriptor in _descriptors.Values)
                    {
                        if (messageDescriptor.MessageType.IsAssignableTo(hookDescriptor.MessageType))
                        {
                            messageDescriptor.AddPreHandleHookDescriptor(hookDescriptor);
                        }
                    }

                    _preHandlerHooks.Add(hookDescriptor);
                }
            }
        }

        public void RegisterPostHandleHook(Type postHandleHookType)
        {
            foreach (var @interface in postHandleHookType.GetInterfaces())
            {
                if (@interface.IsGenericType &&
                    @interface.GetGenericTypeDefinition().IsAssignableTo(typeof(IPostHandleHook<>)))
                {
                    var messageType = @interface.GetGenericArguments()[0];

                    var hookDescriptor = new PostHandleHookDescriptor(postHandleHookType, messageType);

                    foreach (var messageDescriptor in _descriptors.Values)
                    {
                        if (messageDescriptor.MessageType.IsAssignableTo(hookDescriptor.MessageType))
                        {
                            messageDescriptor.AddPostHandleHookDescriptor(hookDescriptor);
                        }
                    }

                    _postHandlerHooks.Add(hookDescriptor);
                }
            }
        }

        private MessageDescriptor GetOrAddMessageDescriptor(Type messageType)
        {
            MessageDescriptor messageDescriptor = default;

            if (_descriptors.ContainsKey(messageType))
            {
                messageDescriptor = _descriptors[messageType] as MessageDescriptor;
            }
            else
            {
                messageDescriptor = new MessageDescriptor(messageType);

                _descriptors[messageType] = messageDescriptor;

                NewMessageDescriptorCreated?.Invoke(this, messageDescriptor);
            }

            return messageDescriptor;
        }

        private void UpdateNewMessagePostHandleHooks(object? sender, MessageDescriptor e)
        {
            foreach (var handleHookDescriptor in _postHandlerHooks)
            {
                if (handleHookDescriptor.MessageType.IsAssignableFrom(e.MessageType))
                {
                    e.AddPostHandleHookDescriptor(handleHookDescriptor);
                }
            }
        }

        private void UpdateNewMessagePreHandleHooks(object? sender, MessageDescriptor e)
        {
            foreach (var handleHookDescriptor in _preHandlerHooks)
            {
                if (handleHookDescriptor.MessageType.IsAssignableFrom(e.MessageType))
                {
                    e.AddPreHandleHookDescriptor(handleHookDescriptor);
                }
            }
        }

        public int Count => _descriptors.Count;
    }
}