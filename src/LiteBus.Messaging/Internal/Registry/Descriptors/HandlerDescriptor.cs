#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Internal.Registry.Descriptors;

internal class HandlerDescriptor : IHandlerDescriptor
{
    public HandlerDescriptor(Type handlerType, Type messageType, Type? messageResultType, Type outputType, int order)
    {
        HandlerType = handlerType;
        IsGeneric = messageType.IsGenericType;
        MessageType = IsGeneric ? messageType.GetGenericTypeDefinition() : messageType;
        MessageResultType = messageResultType;
        OutputType = outputType;
        Order = order;

        if (OutputType.IsAssignableTo(typeof(Task)))
        {
            ExecutionMode = ExecutionMode.Asynchronous;
        }
        else if (OutputType.IsGenericType &&
                 OutputType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
        {
            ExecutionMode = ExecutionMode.AsynchronousStreaming;
        }
        else
        {
            ExecutionMode = ExecutionMode.Synchronous;
        }
    }

    public int Order { get; }

    public ExecutionMode ExecutionMode { get; }

    public Type HandlerType { get; }

    public Type MessageType { get; }

    public Type? MessageResultType { get; }

    public Type OutputType { get; }

    public bool IsGeneric { get; }
}