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

namespace LiteBus.Messaging.Internal.Registry;

internal class MessageRegistry : IMessageRegistry
{
    private readonly List<IDescriptorBuilder> _descriptorBuilders;
    private readonly List<MessageDescriptor> _messages = new();
    private readonly List<IDescriptor> _descriptors = new();
    private readonly ConcurrentDictionary<Type, byte> _processedTypes = new();
    private readonly List<MessageDescriptor> _newMessages = new();

    public MessageRegistry()
    {
        _descriptorBuilders = new List<IDescriptorBuilder>
        {
            new HandlerDescriptorBuilder(),
            new ErrorHandlerDescriptorBuilder(),
            new PostHandlerDescriptorBuilder(),
            new PreHandlerDescriptorBuilder()
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

        var newDescriptors = _descriptorBuilders
                             .Where(d => d.CanBuild(type))
                             .SelectMany(d => d.Build(type))
                             .ToList();

        foreach (var descriptor in newDescriptors)
        {
            RegisterMessage(descriptor.MessageType);
            _descriptors.Add(descriptor);
        }

        // Sync New Messages
        foreach (var messageDescriptor in _newMessages)
        {
            messageDescriptor.AddDescriptors(_descriptors);
        }

        // Sync Existing Messages
        foreach (var messageDescriptor in _messages)
        {
            messageDescriptor.AddDescriptors(newDescriptors);
        }

        _processedTypes[type] = new byte();
        _messages.AddRange(_newMessages);
        _newMessages.Clear();

        return this;
    }

    private void RegisterMessage(Type messageType)
    {
        if (messageType.IsInterface || messageType.IsAbstract)
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