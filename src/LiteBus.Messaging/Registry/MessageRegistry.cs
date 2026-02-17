using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry.Abstractions;
using LiteBus.Messaging.Registry.Builders;
using LiteBus.Messaging.Registry.Descriptors;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Default implementation of IMessageRegistry that provides both message-centric
///     and handler-centric views of registered components.
/// </summary>
/// <remarks>
///     This implementation maintains two complementary views of the data:
///     1. Message-centric: Message descriptors grouped by message type (main interface)
///     2. Handler-centric: Ordered list of handler descriptors for indexed access (Handlers property)
///     The handler-centric view enables efficient change tracking by modules using Count as index.
/// </remarks>
internal sealed class MessageRegistry : IMessageRegistry
{
    // Message descriptors grouped by message type
    private readonly List<MessageDescriptor> _committedMessages = [];

    // Handler descriptor builders for analyzing types
    private readonly List<IHandlerDescriptorBuilder> _descriptorBuilders =
    [
        new HandlerDescriptorBuilder(),
        new ErrorHandlerDescriptorBuilder(),
        new PostHandlerDescriptorBuilder(),
        new PreHandlerDescriptorBuilder()
    ];

    // Handler descriptors in registration order for indexed access
    private readonly List<IHandlerDescriptor> _handlerDescriptorsInOrder = [];

    // Lock for thread safety during collection modifications
    private readonly object _lock = new();
    private readonly List<MessageDescriptor> _pendingMessages = [];

    // Open generic handler types for JIT resolution (e.g., GenericValidator<T> : ICommandPreHandler<T>)
    private readonly List<Type> _openGenericHandlers = [];

    // Cache for processed types to avoid duplicate analysis
    private readonly ConcurrentDictionary<Type, byte> _processedTypes = new();

    /// <inheritdoc />
    public IReadOnlyList<IHandlerDescriptor> Handlers => _handlerDescriptorsInOrder.AsReadOnly();

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _committedMessages.Count;
            }
        }
    }

    /// <inheritdoc />
    public IEnumerator<IMessageDescriptor> GetEnumerator()
    {
        lock (_lock)
        {
            // Create a snapshot to avoid modification during enumeration
            return _committedMessages.Cast<IMessageDescriptor>().ToList().GetEnumerator();
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public void Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        lock (_lock)
        {
            // Skip if already processed to avoid duplicate work
            if (_processedTypes.ContainsKey(type))
                return;

            // Analyze the type using all available descriptor builders
            var newDescriptors = _descriptorBuilders
                .Where(builder => builder.CanBuild(type))
                .SelectMany(builder => builder.Build(type))
                .ToList();

            if (newDescriptors.Count == 0)
            {
                // Type doesn't contain handlers, but might be a message type
                RegisterMessageType(type);
            }
            else if (type.IsGenericTypeDefinition && newDescriptors.Any(d => d.MessageType.IsGenericParameter))
            {
                // This is an open generic handler (e.g., GenericValidator<T> : ICommandPreHandler<T>)
                // where T is a bare type parameter, not a constructed generic like LogActivityCommand<T>.
                // Store it for JIT resolution when concrete message types are registered.
                StoreOpenGenericHandler(type);
            }
            else
            {
                // Type contains handlers - process them
                ProcessHandlerDescriptors(newDescriptors);
            }

            // Ensure pending messages are linked with all existing handlers
            LinkHandlersToPendingMessages();

            // Mark type as processed
            _processedTypes[type] = 0;

            // Commit any pending message descriptors
            CommitPendingMessages();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            _handlerDescriptorsInOrder.Clear();
            _committedMessages.Clear();
            _pendingMessages.Clear();
            _processedTypes.Clear();
            _openGenericHandlers.Clear();
        }
    }

    /// <summary>
    ///     Processes newly discovered handler descriptors by adding them to the handler collection
    ///     and linking them to existing message descriptors.
    /// </summary>
    /// <param name="newDescriptors">The handler descriptors to process.</param>
    private void ProcessHandlerDescriptors(IList<IHandlerDescriptor> newDescriptors)
    {
        foreach (var descriptor in newDescriptors)
        {
            // Ensure the handler's message type is registered
            RegisterMessageType(descriptor.MessageType);

            // Add to ordered list for indexed access
            _handlerDescriptorsInOrder.Add(descriptor);
        }

        // Link new handlers to existing committed messages
        LinkHandlersToCommittedMessages(newDescriptors);
    }

    /// <summary>
    ///     Registers a message type if it hasn't been registered yet.
    /// </summary>
    /// <param name="messageType">The message type to register.</param>
    private void RegisterMessageType(Type messageType)
    {
        // Skip system types to avoid unnecessary processing
        if (messageType.Namespace is not null && messageType.Namespace.StartsWith("System"))
            return;

        // Normalize generic types to their generic type definition
        var normalizedType = messageType.IsGenericType
            ? messageType.GetGenericTypeDefinition()
            : messageType;

        // Check if already exists in committed messages (create snapshot to avoid enumeration issues)
        var committedSnapshot = _committedMessages.ToList();

        if (committedSnapshot.Any(m => m.MessageType == normalizedType))
            return;

        // Check if already exists in pending messages (create snapshot to avoid enumeration issues)
        var pendingSnapshot = _pendingMessages.ToList();

        if (pendingSnapshot.Any(m => m.MessageType == normalizedType))
            return;

        // Add to pending messages
        var descriptor = new MessageDescriptor(normalizedType);
        _pendingMessages.Add(descriptor);

        // Try to close open generic handlers for this concrete message type.
        // Don't link directly - LinkHandlersToPendingMessages will handle it since this is pending.
        if (!normalizedType.IsGenericTypeDefinition)
        {
            foreach (var openGenericHandler in _openGenericHandlers.ToList())
            {
                TryCloseOpenGenericHandler(openGenericHandler, descriptor, linkToMessageDescriptor: false);
            }
        }
    }

    /// <summary>
    ///     Links newly discovered handler descriptors to existing committed message descriptors
    ///     that can be processed by those handlers.
    /// </summary>
    /// <param name="newDescriptors">The new handler descriptors to link.</param>
    private void LinkHandlersToCommittedMessages(IList<IHandlerDescriptor> newDescriptors)
    {
        if (newDescriptors.Count > 0 && _committedMessages.Count > 0)
        {
            // Create snapshot to avoid modification during enumeration
            var committedSnapshot = _committedMessages.ToList();

            foreach (var messageDescriptor in committedSnapshot)
            {
                messageDescriptor.AddDescriptors(newDescriptors);
            }
        }
    }

    /// <summary>
    ///     Links all existing handler descriptors to pending message descriptors.
    /// </summary>
    private void LinkHandlersToPendingMessages()
    {
        if (_pendingMessages.Count > 0 && _handlerDescriptorsInOrder.Count > 0)
        {
            // Create snapshot to avoid modification during enumeration
            var pendingSnapshot = _pendingMessages.ToList();

            foreach (var messageDescriptor in pendingSnapshot)
            {
                messageDescriptor.AddDescriptors(_handlerDescriptorsInOrder);
            }
        }
    }

    /// <summary>
    ///     Stores an open generic handler type and retroactively closes it for all already-known message types.
    /// </summary>
    /// <param name="openGenericHandlerType">The open generic handler type (e.g., GenericValidator&lt;&gt;).</param>
    private void StoreOpenGenericHandler(Type openGenericHandlerType)
    {
        _openGenericHandlers.Add(openGenericHandlerType);

        // Close for committed messages - must add directly since LinkHandlersToPendingMessages won't touch them
        foreach (var messageDescriptor in _committedMessages.ToList())
        {
            TryCloseOpenGenericHandler(openGenericHandlerType, messageDescriptor, linkToMessageDescriptor: true);
        }

        // Close for pending messages - only add to _handlerDescriptorsInOrder.
        // LinkHandlersToPendingMessages (called later in Register) will link them.
        foreach (var messageDescriptor in _pendingMessages.ToList())
        {
            TryCloseOpenGenericHandler(openGenericHandlerType, messageDescriptor, linkToMessageDescriptor: false);
        }
    }

    /// <summary>
    ///     Attempts to close an open generic handler type for a specific message type.
    ///     Always adds closed descriptors to <see cref="_handlerDescriptorsInOrder"/> for DI registration.
    ///     Optionally links them directly to the message descriptor (for committed messages only,
    ///     since pending messages will be linked by <see cref="LinkHandlersToPendingMessages"/>).
    /// </summary>
    /// <param name="openGenericHandlerType">The open generic handler type definition.</param>
    /// <param name="messageDescriptor">The message descriptor to potentially add closed handler descriptors to.</param>
    /// <param name="linkToMessageDescriptor">
    ///     If true, directly adds descriptors to the message descriptor.
    ///     Set to true for committed messages, false for pending messages (to avoid double-linking).
    /// </param>
    private void TryCloseOpenGenericHandler(Type openGenericHandlerType, MessageDescriptor messageDescriptor, bool linkToMessageDescriptor)
    {
        var messageType = messageDescriptor.MessageType;

        // Can't close for open generic message types or generic parameters
        if (messageType.IsGenericTypeDefinition || messageType.IsGenericParameter)
            return;

        // Currently only support single type parameter open generics
        var typeParams = openGenericHandlerType.GetGenericArguments();
        if (typeParams.Length != 1)
            return;

        // Check if the message type satisfies the generic constraints
        if (!SatisfiesGenericConstraints(typeParams[0], messageType))
            return;

        try
        {
            // Close the generic type (e.g., GenericValidator<CreateProductCommand>)
            var closedHandlerType = openGenericHandlerType.MakeGenericType(messageType);

            // Build descriptors for the closed type
            var closedDescriptors = _descriptorBuilders
                .Where(b => b.CanBuild(closedHandlerType))
                .SelectMany(b => b.Build(closedHandlerType))
                .ToList();

            // Add to the ordered handler list for DI registration
            foreach (var descriptor in closedDescriptors)
            {
                _handlerDescriptorsInOrder.Add(descriptor);
            }

            // Link to the message descriptor only if requested (for committed messages)
            if (linkToMessageDescriptor)
            {
                messageDescriptor.AddDescriptors(closedDescriptors);
            }
        }
        catch (ArgumentException)
        {
            // MakeGenericType may throw if constraints can't be satisfied at runtime
        }
    }

    /// <summary>
    ///     Checks whether a candidate type satisfies all the generic parameter constraints
    ///     of the specified type parameter.
    /// </summary>
    /// <param name="typeParameter">The generic type parameter with constraints to check.</param>
    /// <param name="candidateType">The concrete type to check against the constraints.</param>
    /// <returns>True if the candidate type satisfies all constraints; otherwise, false.</returns>
    private static bool SatisfiesGenericConstraints(Type typeParameter, Type candidateType)
    {
        // Check type constraints (e.g., where T : ICommand)
        var constraints = typeParameter.GetGenericParameterConstraints();
        foreach (var constraint in constraints)
        {
            if (!candidateType.IsAssignableTo(constraint))
                return false;
        }

        // Check special constraints (class, struct, new())
        var attributes = typeParameter.GenericParameterAttributes;

        if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0 && candidateType.IsValueType)
            return false;

        if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 && !candidateType.IsValueType)
            return false;

        if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0
            && candidateType.GetConstructor(Type.EmptyTypes) == null
            && !candidateType.IsValueType)
            return false;

        return true;
    }

    /// <summary>
    ///     Commits pending message descriptors to the main collection.
    /// </summary>
    private void CommitPendingMessages()
    {
        if (_pendingMessages.Count > 0)
        {
            _committedMessages.AddRange(_pendingMessages);
            _pendingMessages.Clear();
        }
    }
}