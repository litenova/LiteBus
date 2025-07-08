using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Registry.Abstractions;
using LiteBus.Messaging.Internal.Registry.Builders;
using LiteBus.Messaging.Internal.Registry.Descriptors;

namespace LiteBus.Messaging.Internal.Registry;

internal sealed class MessageRegistry : IMessageRegistry
{
    private readonly List<IHandlerDescriptorBuilder> _descriptorBuilders =
    [
        new HandlerDescriptorBuilder(),
        new ErrorHandlerDescriptorBuilder(),
        new PostHandlerDescriptorBuilder(),
        new PreHandlerDescriptorBuilder()
    ];

    private readonly List<IHandlerDescriptor> _descriptors = [];
    private readonly List<MessageDescriptor> _messages = [];
    private readonly List<MessageDescriptor> _newMessages = [];
    private readonly ConcurrentDictionary<Type, byte> _processedTypes = new();

    public IEnumerator<IMessageDescriptor> GetEnumerator() => _messages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => _messages.Count;

    /// <inheritdoc />
    public void Register(Type type)
    {
        if (_processedTypes.ContainsKey(type))
        {
            return;
        }

        var newDescriptors = _descriptorBuilders
            .Where(d => d.CanBuild(type))
            .SelectMany(d => d.Build(type))
            .ToList();

        // If no descriptor found, treat the type as a message
        if (newDescriptors.Count == 0)
        {
            RegisterMessage(type);
        }
        else
        {
            foreach (var descriptor in newDescriptors)
            {
                RegisterMessage(descriptor.MessageType);
                _descriptors.Add(descriptor);
            }

            // Sync existing messages with new descriptors (if any)
            if (newDescriptors.Count > 0 && _messages.Count > 0)
            {
                foreach (var messageDescriptor in _messages)
                {
                    messageDescriptor.AddDescriptors(newDescriptors);
                }
            }
        }

        // Sync new messages with all descriptors (if any)
        if (_newMessages.Count > 0 && _descriptors.Count > 0)
        {
            foreach (var messageDescriptor in _newMessages)
            {
                messageDescriptor.AddDescriptors(_descriptors);
            }
        }

        _processedTypes[type] = 0;

        if (_newMessages.Count > 0)
        {
            _messages.AddRange(_newMessages);
            _newMessages.Clear();
        }
    }

    private void RegisterMessage(Type messageType)
    {
        if (messageType.Namespace is not null && messageType.Namespace.StartsWith("System"))
        {
            return;
        }

        messageType = messageType.IsGenericType ? messageType.GetGenericTypeDefinition() : messageType;

        // Check if this message type is already registered
        var existingMessage = _messages.FirstOrDefault(d => d.MessageType == messageType);

        if (existingMessage != null)
        {
            return;
        }

        var isNewMessage = _newMessages.FirstOrDefault(d => d.MessageType == messageType);

        if (isNewMessage != null)
        {
            return;
        }

        _newMessages.Add(new MessageDescriptor(messageType));
    }
}