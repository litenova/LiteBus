using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class MessageRegistry : IMessageRegistry
    {
        private readonly ConcurrentDictionary<Type, MessageDescriptor> _messageDescriptors = new();
        private readonly ConcurrentDictionary<Type, PostHandlerDescriptor> _postHandlers = new();
        private readonly ConcurrentDictionary<Type, PreHandlerDescriptor> _preHandlers = new();
        private readonly ConcurrentDictionary<Type, ErrorHandlerDescriptor> _errorHandlers = new();

        public MessageRegistry()
        {
            NewMessageDescriptorCreated += UpdateNewMessagePostHandlers;
            NewMessageDescriptorCreated += UpdateNewMessagePreHandlers;
            NewMessageDescriptorCreated += UpdateNewMessageErrorHandlers;
        }

        public int Count => _messageDescriptors.Count;

        public IEnumerator<IMessageDescriptor> GetEnumerator()
        {
            return _messageDescriptors.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void RegisterHandler(Type handlerType)
        {
            foreach (var @interface in handlerType.GetInterfaces())
            {
                if (@interface.IsGenericType &&
                    @interface.GetGenericTypeDefinition().IsAssignableTo(typeof(IMessageHandler<,>)))
                {
                    var messageType = @interface.GetGenericArguments()[0];
                    var handlerDescriptor = new HandlerDescriptor
                    {
                        HandlerType = handlerType.IsGenericType ? handlerType.GetGenericTypeDefinition() : handlerType,
                        MessageType = messageType.IsGenericType ? messageType.GetGenericTypeDefinition() : messageType,
                        MessageResultType = @interface.GetGenericArguments()[1],
                        IsGeneric = handlerType.IsGenericType
                    };

                    var messageDescriptor = GetOrAddMessageDescriptor(handlerDescriptor.MessageType);

                    messageDescriptor.AddHandlerDescriptor(handlerDescriptor);
                }
            }
        }

        public void RegisterPreHandler(Type preHandlerType)
        {
            if (_preHandlers.ContainsKey(preHandlerType))
            {
                return;
            }

            foreach (var @interface in preHandlerType.GetInterfaces())
            {
                if (@interface.IsGenericType &&
                    @interface.GetGenericTypeDefinition().IsAssignableTo(typeof(IMessagePreHandler<>)))
                {
                    var messageType = @interface.GetGenericArguments()[0];

                    var preHandlerDescriptor = new PreHandlerDescriptor(preHandlerType, messageType);

                    foreach (var messageDescriptor in _messageDescriptors.Values)
                    {
                        if (messageDescriptor.MessageType.IsAssignableTo(preHandlerDescriptor.MessageType))
                        {
                            messageDescriptor.AddPreHandler(preHandlerDescriptor);
                        }
                    }

                    _preHandlers[preHandlerType] = preHandlerDescriptor;
                }
            }
        }
        
        public void RegisterErrorHandler(Type errorHandlerType)
        {
            if (_errorHandlers.ContainsKey(errorHandlerType))
            {
                return;
            }

            foreach (var @interface in errorHandlerType.GetInterfaces())
            {
                if (@interface.IsGenericType &&
                    @interface.GetGenericTypeDefinition().IsAssignableTo(typeof(IMessageErrorHandler<>)))
                {
                    var messageType = @interface.GetGenericArguments()[0];

                    var errorHandlerDescriptor = new ErrorHandlerDescriptor(errorHandlerType, messageType);

                    foreach (var messageDescriptor in _messageDescriptors.Values)
                    {
                        if (messageDescriptor.MessageType.IsAssignableTo(errorHandlerDescriptor.MessageType))
                        {
                            messageDescriptor.AddErrorHandler(errorHandlerDescriptor);
                        }
                    }

                    _errorHandlers[errorHandlerType] = errorHandlerDescriptor;
                }
            }
        }

        public void RegisterPostHandler(Type postHandlerType)
        {
            if (_postHandlers.ContainsKey(postHandlerType))
            {
                return;
            }

            foreach (var @interface in postHandlerType.GetInterfaces())
            {
                if (@interface.IsGenericType && @interface.IsAssignableTo(typeof(IMessagePostHandler)))
                {
                    Type messageType = @interface.GetGenericArguments()[0];
                    Type messageResultType = null;

                    if (@interface.GenericTypeArguments.Length > 1)
                    {
                        messageResultType = @interface.GetGenericArguments()[1];
                    }

                    var postHandlerDescriptor =
                        new PostHandlerDescriptor(postHandlerType, messageType, messageResultType);

                    foreach (var messageDescriptor in _messageDescriptors.Values)
                    {
                        if (messageDescriptor.MessageType.IsAssignableTo(postHandlerDescriptor.MessageType))
                        {
                            messageDescriptor.AddPostHandler(postHandlerDescriptor);
                        }
                    }

                    _postHandlers[postHandlerType] = postHandlerDescriptor;
                }
            }
        }

        private event EventHandler<MessageDescriptor> NewMessageDescriptorCreated;

        private MessageDescriptor GetOrAddMessageDescriptor(Type messageType)
        {
            if (!_messageDescriptors.TryGetValue(messageType, out var descriptor))
            {
                descriptor = new MessageDescriptor(messageType);
                _messageDescriptors[messageType] = descriptor;
                NewMessageDescriptorCreated?.Invoke(this, descriptor);
            }

            return descriptor;
        }

        private void UpdateNewMessagePostHandlers(object? sender, MessageDescriptor e)
        {
            foreach (var postHandlerDescriptor in _postHandlers.Values)
            {
                if (postHandlerDescriptor.MessageType.IsAssignableFrom(e.MessageType))
                {
                    e.AddPostHandler(postHandlerDescriptor);
                }
            }
        }

        private void UpdateNewMessagePreHandlers(object? sender, MessageDescriptor e)
        {
            foreach (var preHandlerDescriptor in _preHandlers.Values)
            {
                if (preHandlerDescriptor.MessageType.IsAssignableFrom(e.MessageType))
                {
                    e.AddPreHandler(preHandlerDescriptor);
                }
            }
        }
        
        private void UpdateNewMessageErrorHandlers(object? sender, MessageDescriptor e)
        {
            foreach (var errorHandlerDescriptor in _errorHandlers.Values)
            {
                if (errorHandlerDescriptor.MessageType.IsAssignableFrom(e.MessageType))
                {
                    e.AddErrorHandler(errorHandlerDescriptor);
                }
            }
        }
    }
}