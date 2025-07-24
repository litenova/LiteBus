using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

internal sealed class MessageDescriptor : IMessageDescriptor
{
    private readonly List<IErrorHandlerDescriptor> _errorHandlers = new();
    private readonly List<IMainHandlerDescriptor> _handlers = new();
    private readonly List<IErrorHandlerDescriptor> _indirectErrorHandlers = new();
    private readonly List<IMainHandlerDescriptor> _indirectHandlers = new();
    private readonly List<IPostHandlerDescriptor> _indirectPostHandlers = new();
    private readonly List<IPreHandlerDescriptor> _indirectPreHandlers = new();
    private readonly List<IPostHandlerDescriptor> _postHandlers = new();
    private readonly List<IPreHandlerDescriptor> _preHandlers = new();

    public MessageDescriptor(Type messageType)
    {
        MessageType = messageType;
        IsGeneric = messageType.IsGenericType;
    }

    public Type MessageType { get; }

    public bool IsGeneric { get; }

    public IReadOnlyCollection<IMainHandlerDescriptor> Handlers => _handlers;

    public IReadOnlyCollection<IMainHandlerDescriptor> IndirectHandlers => _indirectHandlers;

    public IReadOnlyCollection<IPostHandlerDescriptor> PostHandlers => _postHandlers;

    public IReadOnlyCollection<IPostHandlerDescriptor> IndirectPostHandlers => _indirectPostHandlers;

    public IReadOnlyCollection<IPreHandlerDescriptor> PreHandlers => _preHandlers;

    public IReadOnlyCollection<IPreHandlerDescriptor> IndirectPreHandlers => _indirectPreHandlers;

    public IReadOnlyCollection<IErrorHandlerDescriptor> ErrorHandlers => _errorHandlers;

    public IReadOnlyCollection<IErrorHandlerDescriptor> IndirectErrorHandlers => _indirectErrorHandlers;

    public void AddDescriptors(IEnumerable<IHandlerDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            AddDescriptor(descriptor);
        }
    }

    public void AddDescriptor(IHandlerDescriptor descriptor)
    {
        if (MessageType == descriptor.MessageType)
        {
            switch (descriptor)
            {
                case IErrorHandlerDescriptor errorHandlerDescriptor:
                    _errorHandlers.Add(errorHandlerDescriptor);
                    break;
                case IMainHandlerDescriptor mainHandlerDescriptor:
                    _handlers.Add(mainHandlerDescriptor);
                    break;
                case IPostHandlerDescriptor postHandlerDescriptor:
                    _postHandlers.Add(postHandlerDescriptor);
                    break;
                case IPreHandlerDescriptor preHandlerDescriptor:
                    _preHandlers.Add(preHandlerDescriptor);
                    break;
            }
        }
        else if (MessageType.IsAssignableTo(descriptor.MessageType))
        {
            switch (descriptor)
            {
                case IErrorHandlerDescriptor errorHandlerDescriptor:
                    _indirectErrorHandlers.Add(errorHandlerDescriptor);
                    break;
                case IMainHandlerDescriptor mainHandlerDescriptor:
                    _indirectHandlers.Add(mainHandlerDescriptor);
                    break;
                case IPostHandlerDescriptor postHandlerDescriptor:
                    _indirectPostHandlers.Add(postHandlerDescriptor);
                    break;
                case IPreHandlerDescriptor preHandlerDescriptor:
                    _indirectPreHandlers.Add(preHandlerDescriptor);
                    break;
            }
        }
    }
}