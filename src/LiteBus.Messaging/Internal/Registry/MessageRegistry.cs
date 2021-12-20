using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;
using LiteBus.Messaging.Internal.Registry.Abstractions;
using LiteBus.Messaging.Internal.Registry.Builders;
using LiteBus.Messaging.Internal.Registry.Descriptors;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class MessageRegistry : IMessageRegistry
    {
        private readonly List<MessageDescriptor> _messages = new();
        private readonly List<IDescriptorBuilder> _descriptorBuilders;
        private readonly ConcurrentDictionary<Type, byte> _processedTypes = new();

        public MessageRegistry()
        {
            _descriptorBuilders = new List<IDescriptorBuilder>
            {
                new HandlerDescriptorBuilder(),
                new ErrorHandlerDescriptorBuilder(),
                new PostHandlerDescriptorBuilder(),
                new PreHandlerDescriptorBuilder(),
            };
        }

        public IEnumerator<IMessageDescriptor> GetEnumerator()
        {
            return _messages.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count => _messages.Count;

        public IMessageRegistry Register(Type type)
        {
            if (_processedTypes.ContainsKey(type))
            {
                return this;
            }

            var descriptors = _descriptorBuilders
                              .Where(d => d.CanBuild(type))
                              .SelectMany(d => d.Build(type));

            foreach (var descriptor in descriptors)
            {
                AddMessageIfNotExist(descriptor.MessageType);

                var relevantMessages = _messages
                    .Where(d => d.MessageType.IsAssignableTo(descriptor.MessageType));

                foreach (var messageDescriptor in relevantMessages)
                {
                    messageDescriptor.AddDescriptor(descriptor);
                }
            }

            _processedTypes[type] = new byte();

            return this;
        }

        private void AddMessageIfNotExist(Type type)
        {
            if (type.IsAbstract || type.IsInterface)
            {
                return;
            }
            
            var existingMessage = _messages.SingleOrDefault(d => d.MessageType == type);

            if (existingMessage is null)
            {
                _messages.Add(new MessageDescriptor(type));
            }
        }
    }
}