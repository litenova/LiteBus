using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

/// <summary>
///     Mutable registry entry that groups handlers for one message type.
/// </summary>
internal sealed class MessageDescriptor : IMessageDescriptor
{
    /// <summary>
    ///     Direct error handlers registered for <see cref="MessageType" />.
    /// </summary>
    private readonly List<IErrorHandlerDescriptor> _errorHandlers = [];

    /// <summary>
    ///     Direct main handlers registered for <see cref="MessageType" />.
    /// </summary>
    private readonly List<IMainHandlerDescriptor> _handlers = [];

    /// <summary>
    ///     Error handlers registered for a base type or interface of <see cref="MessageType" />.
    /// </summary>
    private readonly List<IErrorHandlerDescriptor> _indirectErrorHandlers = [];

    /// <summary>
    ///     Main handlers registered for a base type or interface of <see cref="MessageType" />.
    /// </summary>
    private readonly List<IMainHandlerDescriptor> _indirectHandlers = [];

    /// <summary>
    ///     Post-handlers registered for a base type or interface of <see cref="MessageType" />.
    /// </summary>
    private readonly List<IPostHandlerDescriptor> _indirectPostHandlers = [];

    /// <summary>
    ///     Pre-handlers registered for a base type or interface of <see cref="MessageType" />.
    /// </summary>
    private readonly List<IPreHandlerDescriptor> _indirectPreHandlers = [];

    /// <summary>
    ///     Direct post-handlers registered for <see cref="MessageType" />.
    /// </summary>
    private readonly List<IPostHandlerDescriptor> _postHandlers = [];

    /// <summary>
    ///     Direct pre-handlers registered for <see cref="MessageType" />.
    /// </summary>
    private readonly List<IPreHandlerDescriptor> _preHandlers = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageDescriptor" /> class.
    /// </summary>
    /// <param name="messageType">The message type represented by this descriptor.</param>
    public MessageDescriptor(Type messageType)
    {
        MessageType = messageType;
        IsGeneric = messageType.IsGenericType;
    }

    /// <inheritdoc />
    public Type MessageType { get; }

    /// <inheritdoc />
    public bool IsGeneric { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<IMainHandlerDescriptor> Handlers => _handlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IMainHandlerDescriptor> IndirectHandlers => _indirectHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IPostHandlerDescriptor> PostHandlers => _postHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IPostHandlerDescriptor> IndirectPostHandlers => _indirectPostHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IPreHandlerDescriptor> PreHandlers => _preHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IPreHandlerDescriptor> IndirectPreHandlers => _indirectPreHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IErrorHandlerDescriptor> ErrorHandlers => _errorHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IErrorHandlerDescriptor> IndirectErrorHandlers => _indirectErrorHandlers;

    /// <summary>
    ///     Adds handler descriptors, routing each to direct or indirect collections.
    /// </summary>
    /// <param name="descriptors">The handler descriptors to associate with this message.</param>
    public void AddDescriptors(IEnumerable<IHandlerDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            AddDescriptor(descriptor);
        }
    }

    /// <summary>
    ///     Adds one handler descriptor, routing it to direct or indirect collections.
    /// </summary>
    /// <param name="descriptor">The handler descriptor to associate with this message.</param>
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
        else if (ImplementsOpenGeneric(descriptor.MessageType, MessageType))
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

    /// <summary>
    ///     Determines whether <paramref name="messageType" /> implements a constructed instance of
    ///     <paramref name="openGenericType" />.
    /// </summary>
    /// <param name="openGenericType">The open generic handler message type.</param>
    /// <param name="messageType">The concrete message type.</param>
    /// <returns><see langword="true" /> when the message implements the open generic contract.</returns>
    private static bool ImplementsOpenGeneric(Type openGenericType, Type messageType)
    {
        if (!openGenericType.IsGenericTypeDefinition)
        {
            return false;
        }

        foreach (var implementedInterface in messageType.GetInterfaces())
        {
            if (implementedInterface.IsGenericType &&
                implementedInterface.GetGenericTypeDefinition() == openGenericType)
            {
                return true;
            }
        }

        return false;
    }
}