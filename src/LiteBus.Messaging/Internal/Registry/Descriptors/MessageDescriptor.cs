using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry.Descriptors;

internal class MessageDescriptor : IMessageDescriptor
{
    private readonly List<IErrorHandlerDescriptor> _errorHandlerDescriptors = new();
    private readonly List<IHandlerDescriptor> _handlerDescriptors = new();
    private readonly List<IPostHandlerDescriptor> _postHandlerDescriptors = new();
    private readonly List<IPreHandlerDescriptor> _preHandlerDescriptors = new();

    public MessageDescriptor(Type messageType)
    {
        MessageType = messageType;
        IsGeneric = messageType.IsGenericType;
    }

    public Type MessageType { get; }

    public bool IsGeneric { get; }

    public IReadOnlyCollection<IHandlerDescriptor> HandlerDescriptors => _handlerDescriptors;

    public IReadOnlyCollection<IPostHandlerDescriptor> PostHandlerDescriptors => _postHandlerDescriptors;

    public IReadOnlyCollection<IPreHandlerDescriptor> PreHandlerDescriptors => _preHandlerDescriptors;

    public IReadOnlyCollection<IErrorHandlerDescriptor> ErrorHandlerDescriptors => _errorHandlerDescriptors;

    public void AddDescriptor(IDescriptor descriptor)
    {
        switch (descriptor)
        {
            case IErrorHandlerDescriptor errorHandlerDescriptor:
                _errorHandlerDescriptors.Add(errorHandlerDescriptor);
                break;
            case IHandlerDescriptor handlerDescriptor:
                _handlerDescriptors.Add(handlerDescriptor);
                break;
            case IPostHandlerDescriptor postHandlerDescriptor:
                _postHandlerDescriptors.Add(postHandlerDescriptor);
                break;
            case IPreHandlerDescriptor preHandlerDescriptor:
                _preHandlerDescriptors.Add(preHandlerDescriptor);
                break;
            default:
                throw new NotSupportedException($"The type '{descriptor.GetType().Name}' cannot be identified");
        }
    }
}