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
    private readonly List<IDescriptorBuilder> _descriptorBuilders = new()
    {
        new HandlerDescriptorBuilder(),
        new ErrorHandlerDescriptorBuilder(),
        new PostHandlerDescriptorBuilder(),
        new PreHandlerDescriptorBuilder()
    };
    private readonly List<MessageDescriptor> _messages = new();
    private readonly List<IDescriptor> _descriptors = new();
    private readonly ConcurrentDictionary<Type, byte> _processedTypes = new();
    private readonly List<MessageDescriptor> _newMessages = new();

    public IEnumerator<IMessageDescriptor> GetEnumerator()
    {
        return _messages.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count => _messages.Count;

    public void Register(Type type)
    {
        if (_processedTypes.ContainsKey(type))
        {
            return;
        }

        var newDescriptors = _descriptorBuilders.Where(d => d.CanBuild(type))
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

            // Sync existing messages with new descriptors
            foreach (var messageDescriptor in _messages)
            {
                messageDescriptor.AddDescriptors(newDescriptors);
            }
        }

        // Sync new messages with all the descriptors
        foreach (var messageDescriptor in _newMessages)
        {
            messageDescriptor.AddDescriptors(_descriptors);
        }

        _processedTypes[type] = new byte();
        _messages.AddRange(_newMessages);
        _newMessages.Clear();
    }

    private void RegisterMessage(Type messageType)
    {
        if (!messageType.IsClass || messageType.Namespace is not null && messageType.Namespace.StartsWith("System"))
        {
            return;
        }

        messageType = messageType.IsGenericType ? messageType.GetGenericTypeDefinition() : messageType;

        var existingMessage = _messages.SingleOrDefault(d => d.MessageType == messageType);
        var isNewMessage = _newMessages.SingleOrDefault(d => d.MessageType == messageType);

        if (existingMessage is null && isNewMessage is null)
        {
            _newMessages.Add(new MessageDescriptor(messageType));
        }
    }
}