using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Messaging.Registry.Abstractions;
using LiteBus.Messaging.Registry.Builders;
using LiteBus.Messaging.Registry.Descriptors;
using LiteBus.Runtime.Abstractions.Exceptions;

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
    /// <summary>
    ///     Message descriptors that have completed registration and handler linking.
    /// </summary>
    private readonly List<MessageDescriptor> _committedMessages = [];

    /// <summary>
    ///     Builders that discover handler descriptors from registered CLR types.
    /// </summary>
    private readonly List<IHandlerDescriptorBuilder> _descriptorBuilders =
    [
        new HandlerDescriptorBuilder(),
        new CompletionHandlerDescriptorBuilder(),
        new ErrorHandlerDescriptorBuilder(),
        new PostHandlerDescriptorBuilder(),
        new PreStageHandlerDescriptorBuilder(),
        new RefusalMapperDescriptorBuilder()
    ];

    /// <summary>
    ///     Committed message descriptors keyed by normalized message type for O(1) exact lookup.
    /// </summary>
    private readonly Dictionary<Type, MessageDescriptor> _descriptorsByType = new();

    /// <summary>
    ///     Handler descriptors in registration order for module incremental DI registration.
    /// </summary>
    private readonly List<IHandlerDescriptor> _handlerDescriptorsInOrder = [];

    /// <summary>
    ///     Synchronizes mutations to registry collections and processed-type tracking.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     Metadata declared by message definitions, applied to descriptors as they are created.
    /// </summary>
    private readonly List<MessageDeclaration> _declarations = [];

    /// <summary>
    ///     Open generic handler definitions waiting to be closed over concrete message types.
    /// </summary>
    private readonly List<Type> _openGenericHandlers = [];

    /// <summary>
    ///     The concrete message types each open generic handler was successfully closed over.
    /// </summary>
    /// <remarks>
    ///     Recorded so composition can report what a scanned open generic actually reached. A handler that closes over
    ///     140 commands and one that closes over none look identical in a registration list, and only the first
    ///     changes what every message does.
    /// </remarks>
    private readonly Dictionary<Type, HashSet<Type>> _openGenericClosures = [];

    /// <summary>
    ///     The open generic handler types that arrived through an assembly scan rather than being named.
    /// </summary>
    /// <remarks>
    ///     Recorded so <c>RequireExplicitOpenGenerics</c> can name them. Scanning is the default and stays it, because
    ///     picking up open generic handlers is what a scan has meant since v4; the strict mode is for a team that
    ///     wants every pipeline-wide stage to appear as a reviewable line in the composition code.
    /// </remarks>
    private readonly HashSet<Type> _scannedOpenGenericHandlers = [];

    /// <summary>
    ///     Gets the open generic handler types an explicit <see cref="Register(Type)" /> call registered.
    /// </summary>
    /// <remarks>
    ///     Held separately from <see cref="_scannedOpenGenericHandlers" /> rather than removed from it, so the two
    ///     orders agree: a scan reaching a handler before its explicit registration and after it both leave the same
    ///     answer. Subtracting one set from the other is what <see cref="ScannedOpenGenericHandlers" /> reports.
    /// </remarks>
    private readonly HashSet<Type> _explicitOpenGenericHandlers = [];

    /// <summary>
    ///     Message descriptors discovered during the current registration pass before commit.
    /// </summary>
    private readonly List<MessageDescriptor> _pendingMessages = [];

    /// <summary>
    ///     Normalized message types registered in the current pass before commit.
    /// </summary>
    private readonly HashSet<Type> _pendingMessageTypes = [];

    /// <summary>
    ///     Tracks CLR types already analyzed to prevent duplicate registration work.
    /// </summary>
    private readonly HashSet<Type> _processedTypes = [];

    /// <summary>
    ///     The next registration sequence value assigned to committed handler descriptors.
    /// </summary>
    private int _nextRegistrationSequence;

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
    public IMessageDescriptor? Find(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        lock (_lock)
        {
            if (_descriptorsByType.TryGetValue(messageType, out var exactDescriptor))
            {
                return exactDescriptor;
            }

            return messageType.IsGenericType
                ? _descriptorsByType.GetValueOrDefault(messageType.GetGenericTypeDefinition())
                : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<Type, IReadOnlyCollection<Type>> OpenGenericClosures
    {
        get
        {
            lock (_lock)
            {
                var snapshot = new Dictionary<Type, IReadOnlyCollection<Type>>(_openGenericClosures.Count);

                foreach (var (handlerType, closures) in _openGenericClosures)
                {
                    snapshot[handlerType] = closures.ToList();
                }

                return snapshot;
            }
        }
    }

    /// <inheritdoc />
    public IEnumerator<IMessageDescriptor> GetEnumerator()
    {
        lock (_lock)
        {
            // Create a snapshot to avoid modification during enumeration.
            return _committedMessages.Cast<IMessageDescriptor>().ToList().GetEnumerator();
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<Type> ScannedOpenGenericHandlers
    {
        get
        {
            lock (_lock)
            {
                // Only the handlers that reached the registry by scanning alone. An explicit Register for the same
                // type is the registration line a reviewer reads, and a scan walking past that type afterwards does
                // not take it away, so the sets are subtracted rather than unioned.
                return _scannedOpenGenericHandlers
                       .Where(handler => !_explicitOpenGenericHandlers.Contains(handler))
                       .ToList();
            }
        }
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Handler and message registration inspects CLR types via reflection.")]
    public void RegisterFromScan(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.PublicMethods
            | DynamicallyAccessedMemberTypes.Interfaces)]
        Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        RegisterCore(type, fromScan: true);
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Handler and message registration inspects CLR types via reflection.")]
    public void Register(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.PublicMethods
            | DynamicallyAccessedMemberTypes.Interfaces)]
        Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        RegisterCore(type, fromScan: false);
    }

    /// <summary>
    ///     Registers a type, recording where an open generic handler came from.
    /// </summary>
    /// <param name="type">The type to register.</param>
    /// <param name="fromScan">
    ///     <see langword="true" /> when an assembly scan produced the type, <see langword="false" /> when a
    ///     registration line named it.
    /// </param>
    /// <remarks>
    ///     The origin is recorded before the processed-types check rather than after it. A handler a scan reached
    ///     first is already processed by the time an explicit registration for it arrives, so recording afterwards
    ///     would drop the registration line and leave a scanned mark that <c>RequireExplicitOpenGenerics</c> could
    ///     never clear.
    /// </remarks>
    [RequiresUnreferencedCode("Handler and message registration inspects CLR types via reflection.")]
    private void RegisterCore(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.PublicMethods
            | DynamicallyAccessedMemberTypes.Interfaces)]
        Type type,
        bool fromScan)
    {
        lock (_lock)
        {
            if (type.IsGenericTypeDefinition)
            {
                var origin = fromScan ? _scannedOpenGenericHandlers : _explicitOpenGenericHandlers;
                origin.Add(type);
            }

            // Skip if already processed to avoid duplicate work.
            if (!_processedTypes.Add(type))
                return;

            // Message definitions declare metadata rather than behavior, so they never produce handler descriptors.
            if (MessageDefinitionBinder.IsDefinition(type))
            {
                RegisterDefinition(type);
                LinkHandlersToPendingMessages();
                ApplyDefinitionsToPendingMessages();
                CommitPendingMessages();
                return;
            }

            // Analyze the type using all available descriptor builders.
            var claimingBuilders = _descriptorBuilders
                .Where(builder => builder.CanBuild(type))
                .ToList();

            var newDescriptors = claimingBuilders
                .SelectMany(builder => builder.Build(type))
                .ToList();

            if (newDescriptors.Count == 0)
            {
                ThrowIfPipelineMarkerExposesNoContract(type, claimingBuilders.Count);

                // Type doesn't contain handlers, but might be a message type.
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
                // Type contains handlers - process them.
                ProcessHandlerDescriptors(newDescriptors);
            }

            // Ensure pending messages are linked with all existing handlers.
            LinkHandlersToPendingMessages();

            // Apply metadata declared by definitions registered before this message type was known.
            ApplyDefinitionsToPendingMessages();

            // Commit any pending message descriptors.
            CommitPendingMessages();
        }
    }

    /// <summary>
    ///     Processes newly discovered handler descriptors by adding them to the handler collection
    ///     and linking them to existing message descriptors.
    /// </summary>
    /// <param name="newDescriptors">The handler descriptors to process.</param>
    private void ProcessHandlerDescriptors(List<IHandlerDescriptor> newDescriptors)
    {
        var committedDescriptors = new List<IHandlerDescriptor>(newDescriptors.Count);

        foreach (var descriptor in newDescriptors)
        {
            // Ensure the handler's message type is registered.
            RegisterMessageType(descriptor.MessageType);

            // Add to ordered list for indexed access.
            var committed = CommitHandlerDescriptor(descriptor);
            _handlerDescriptorsInOrder.Add(committed);
            committedDescriptors.Add(committed);
        }

        // Link new handlers to existing committed messages.
        LinkHandlersToCommittedMessages(committedDescriptors);
    }

    /// <inheritdoc />
    public void AddDeclaration(MessageDeclarationItem declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        if (!declaration.DeclarationType.IsInstanceOfType(declaration.Value))
        {
            throw new MessageDeclarationException(
                $"The declaration of '{declaration.DeclarationType.Name}' for '{declaration.MessageType.Name}' "
                + $"supplied a '{declaration.Value.GetType().Name}', which is not assignable to it. A value has to be "
                + "an instance of the type it is keyed by, or a reader looking it up by that type finds nothing.");
        }

        lock (_lock)
        {
            Apply(new MessageDeclaration(
                declaration.MessageType.NormalizeMessageRegistrationType(),
                declaration.DeclarationType,
                declaration.Value,
                DefinitionType: null));
        }
    }

    /// <summary>
    ///     Binds a message definition type and applies what it declares to known and pending message descriptors.
    /// </summary>
    /// <param name="definitionType">The concrete message definition type.</param>
    [RequiresUnreferencedCode("Message definition binding reads declaration contracts via reflection.")]
    private void RegisterDefinition(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.NonPublicConstructors
            | DynamicallyAccessedMemberTypes.Interfaces)]
        Type definitionType)
    {
        foreach (var declaration in MessageDefinitionBinder.Bind(definitionType))
        {
            Apply(declaration);
        }
    }

    /// <summary>
    ///     Records one declaration and applies it to every message it covers.
    /// </summary>
    /// <param name="declaration">The declaration to record.</param>
    /// <remarks>
    ///     Shared by a definition class and a declaration made from composition code, so both resolve by the same
    ///     rules. A family default declared here is overridden by a message that states its own position, because
    ///     that precedence lives in the metadata bag rather than in either caller.
    /// </remarks>
    private void Apply(MessageDeclaration declaration)
    {
        ThrowIfAlreadyDeclared(declaration);
        _declarations.Add(declaration);

        // The described message may not have been registered yet.
        RegisterMessageType(declaration.MessageType);

        // A declaration written for a base type or interface covers every message beneath it, including messages
        // that were committed before the declaration arrived.
        foreach (var committed in _committedMessages)
        {
            ApplyDeclaration(declaration, committed);
        }

        // And messages awaiting commit, which is where a message registered in the same pass sits.
        ApplyDefinitionsToPendingMessages();
        CommitPendingMessages();
    }

    /// <summary>
    ///     Reports a second definition declaring the same value type for the same message.
    /// </summary>
    /// <param name="declaration">The declaration being registered.</param>
    /// <exception cref="PipelineContractException">
    ///     Thrown when another definition already declared this value type for this message.
    /// </exception>
    /// <remarks>
    ///     Definitions are applied in an order nobody controls, so letting the last one win would make the effective
    ///     configuration depend on assembly scanning order. Reporting it at registration keeps the declaration single.
    /// </remarks>
    private void ThrowIfAlreadyDeclared(MessageDeclaration declaration)
    {
        foreach (var existing in _declarations)
        {
            if (existing.MessageType == declaration.MessageType && existing.KeyType == declaration.KeyType)
            {
                throw new PipelineContractException(
                    $"Both '{existing.SourceName}' and '{declaration.SourceName}' declare "
                    + $"'{declaration.KeyType.Name}' for the message '{declaration.MessageType.Name}'. "
                    + "Keep one declaration per message and value type.");
            }
        }
    }

    /// <summary>
    ///     Applies every known declaration to message descriptors awaiting commit.
    /// </summary>
    private void ApplyDefinitionsToPendingMessages()
    {
        if (_pendingMessages.Count == 0 || _declarations.Count == 0)
        {
            return;
        }

        foreach (var messageDescriptor in _pendingMessages)
        {
            foreach (var declaration in _declarations)
            {
                ApplyDeclaration(declaration, messageDescriptor);
            }
        }
    }

    /// <summary>
    ///     Applies one declaration to a message descriptor when the declaration covers that message.
    /// </summary>
    /// <param name="declaration">The declaration contributed by a definition.</param>
    /// <param name="messageDescriptor">The descriptor to apply it to.</param>
    /// <remarks>
    ///     A declaration covers the message it names and every message assignable to it, which lets one definition
    ///     describe a family of messages through a base type or marker interface. Open generic message shapes are
    ///     matched exactly, because assignability is not meaningful between generic type definitions.
    /// </remarks>
    private static void ApplyDeclaration(MessageDeclaration declaration, MessageDescriptor messageDescriptor)
    {
        if (!Covers(declaration.MessageType, messageDescriptor.MessageType))
        {
            return;
        }

        messageDescriptor.ApplyMetadata(declaration.KeyType, declaration.Value, declaration.MessageType);
    }

    /// <summary>
    ///     Determines whether a declaration written for one message type covers another.
    /// </summary>
    /// <param name="declaredFor">The message type the declaration names.</param>
    /// <param name="messageType">The message type being described.</param>
    /// <returns><see langword="true" /> when the declaration applies to the message type.</returns>
    private static bool Covers(Type declaredFor, Type messageType)
    {
        if (declaredFor == messageType)
        {
            return true;
        }

        if (declaredFor.ContainsGenericParameters || messageType.ContainsGenericParameters)
        {
            return false;
        }

        return declaredFor.IsAssignableFrom(messageType);
    }

    /// <summary>
    ///     Registers a message type if it hasn't been registered yet.
    /// </summary>
    /// <param name="messageType">The message type to register.</param>
    private void RegisterMessageType(Type messageType)
    {
        // Skip system types to avoid unnecessary processing.
        if (IsSystemNamespace(messageType))
            return;

        var normalizedType = messageType.NormalizeMessageRegistrationType();

        if (_descriptorsByType.ContainsKey(normalizedType) || !_pendingMessageTypes.Add(normalizedType))
            return;

        // Add to pending messages.
        var descriptor = new MessageDescriptor(normalizedType);
        _pendingMessages.Add(descriptor);

        // Try to close open generic handlers for this concrete message type.
        // Don't link directly - LinkHandlersToPendingMessages will handle it since this is pending.
        if (!normalizedType.IsGenericTypeDefinition)
        {
            foreach (var openGenericHandler in _openGenericHandlers.ToList())
            {
                TryCloseOpenGenericHandler(openGenericHandler, descriptor, false);
            }
        }
    }

    /// <summary>
    ///     Links newly discovered handler descriptors to existing committed message descriptors
    ///     that can be processed by those handlers.
    /// </summary>
    /// <param name="newDescriptors">The new handler descriptors to link.</param>
    private void LinkHandlersToCommittedMessages(List<IHandlerDescriptor> newDescriptors)
    {
        if (newDescriptors.Count == 0 || _committedMessages.Count == 0)
        {
            return;
        }

        var committedSnapshot = _committedMessages.ToList();

        foreach (var handlerDescriptor in newDescriptors)
        {
            foreach (var messageDescriptor in committedSnapshot)
            {
                messageDescriptor.AddDescriptor(handlerDescriptor);
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
            // Create snapshot to avoid modification during enumeration.
            var pendingSnapshot = _pendingMessages.ToList();

            foreach (var messageDescriptor in pendingSnapshot)
            {
                messageDescriptor.AddDescriptors(_handlerDescriptorsInOrder);
            }
        }
    }

    /// <summary>
    ///     The messaging-level handler contracts whose second type argument is the message's result type.
    /// </summary>
    /// <remarks>
    ///     Used to tell an arity-2 handler that binds a result type apart from one that declares a second parameter of
    ///     its own, which has nothing to bind to and cannot be closed.
    /// </remarks>
    private static readonly HashSet<Type> TypedHandlerContracts =
    [
        typeof(IMessageHandler<,>),
        typeof(IStreamMessageHandler<,>),
        typeof(IMessagePostHandler<,>),
        typeof(IMessageCompletionHandler<,>),
        typeof(IMessageErrorHandler<,>),
        typeof(IMessageShortcut<,>),
        typeof(IMessageRefusalMapper<,>)
    ];

    /// <summary>
    ///     Stores an open generic handler type and retroactively closes it for all already-known message types.
    /// </summary>
    /// <param name="openGenericHandlerType">The open generic handler type (e.g., GenericValidator&lt;&gt;).</param>
    private void StoreOpenGenericHandler(Type openGenericHandlerType)
    {
        ThrowIfOpenGenericHandlerShapeIsUnsupported(openGenericHandlerType);

        _openGenericHandlers.Add(openGenericHandlerType);

        // Recorded on registration, so an open generic that fits nothing still appears in the composition summary as
        // covering zero messages. A handler that silently never runs is the case the summary most needs to show.
        _openGenericClosures.TryAdd(openGenericHandlerType, []);

        // Close for committed messages - must add directly since LinkHandlersToPendingMessages won't touch them.
        foreach (var messageDescriptor in _committedMessages.ToList())
        {
            TryCloseOpenGenericHandler(openGenericHandlerType, messageDescriptor, true);
        }

        // Close for pending messages - only add to _handlerDescriptorsInOrder.
        // LinkHandlersToPendingMessages (called later in Register) will link them.
        foreach (var messageDescriptor in _pendingMessages.ToList())
        {
            TryCloseOpenGenericHandler(openGenericHandlerType, messageDescriptor, false);
        }
    }

    /// <summary>
    ///     Attempts to close an open generic handler type for a specific message type.
    ///     Always adds closed descriptors to <see cref="_handlerDescriptorsInOrder" /> for DI registration.
    ///     Optionally links them directly to the message descriptor (for committed messages only,
    ///     since pending messages will be linked by <see cref="LinkHandlersToPendingMessages" />).
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

        // Can't close for open generic message types or generic parameters.
        if (messageType.IsGenericTypeDefinition || messageType.IsGenericParameter)
            return;

        var typeArguments = BuildOpenGenericTypeArguments(openGenericHandlerType, messageType);

        if (typeArguments is null || !CanCloseForMessage(openGenericHandlerType, messageType))
        {
            return;
        }

        try
        {
            // Let the CLR evaluate substituted constraints such as IComparable<T>, which cannot be tested correctly
            // against the unresolved generic parameter with Type.IsAssignableTo.
            var closedHandlerType = openGenericHandlerType.MakeGenericType(typeArguments);

            // Build descriptors for the closed type.
            var closedDescriptors = _descriptorBuilders
                .Where(b => b.CanBuild(closedHandlerType))
                .SelectMany(b => b.Build(closedHandlerType))
                .ToList();

            // Add to the ordered handler list for DI registration.
            var committedDescriptors = new List<IHandlerDescriptor>(closedDescriptors.Count);

            foreach (var descriptor in closedDescriptors)
            {
                var committed = CommitHandlerDescriptor(descriptor);
                committedDescriptors.Add(committed);
                _handlerDescriptorsInOrder.Add(committed);
            }

            // Link to the message descriptor only if requested (for committed messages)
            if (linkToMessageDescriptor)
            {
                messageDescriptor.AddDescriptors(committedDescriptors);
            }

            if (closedDescriptors.Count > 0)
            {
                if (!_openGenericClosures.TryGetValue(openGenericHandlerType, out var closures))
                {
                    closures = [];
                    _openGenericClosures[openGenericHandlerType] = closures;
                }

                closures.Add(messageType);
            }
        }
        catch (ArgumentException)
        {
            // The concrete message does not satisfy the handler's complete generic constraint set.
        }
    }

    /// <summary>
    ///     Builds the type arguments that close an open generic handler for one concrete message type.
    /// </summary>
    /// <param name="openGenericHandlerType">The open generic handler type definition.</param>
    /// <param name="messageType">The concrete message type being closed for.</param>
    /// <returns>
    ///     The type arguments to substitute, or <see langword="null" /> when this handler cannot describe this message.
    /// </returns>
    /// <remarks>
    ///     An arity-2 handler needs the message's declared result type, so it is simply not closed for a message that
    ///     declares none. That is the same silence a constraint mismatch produces: a generic handler covers the messages
    ///     it fits, and a void command is not one of them.
    /// </remarks>
    private static Type[]? BuildOpenGenericTypeArguments(Type openGenericHandlerType, Type messageType)
    {
        if (openGenericHandlerType.GetGenericArguments().Length == 1)
        {
            return [messageType];
        }

        var resultType = FindDeclaredResultType(messageType);

        return resultType is null ? null : [messageType, resultType];
    }

    /// <summary>
    ///     Determines whether an open generic handler describes a message it can legally answer for.
    /// </summary>
    /// <param name="openGenericHandlerType">The open generic handler type definition.</param>
    /// <param name="messageType">The concrete message type being closed for.</param>
    /// <returns><see langword="false" /> when closing would produce a registration the pipeline rejects.</returns>
    /// <remarks>
    ///     An untyped shortcut cannot answer a message that produces a result, and a closed registration of that
    ///     combination is reported as a configuration error because the author named the message. An open generic says
    ///     "every message I fit" instead, and a result-producing message is one it does not fit, so it is skipped the
    ///     way a constraint mismatch is. That is what lets one generic shortcut cover the void messages in an axis
    ///     without exploding on the ones that return something.
    /// </remarks>
    private static bool CanCloseForMessage(Type openGenericHandlerType, Type messageType)
    {
        if (FindDeclaredResultType(messageType) is null)
        {
            return true;
        }

        foreach (var contract in openGenericHandlerType.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IMessageShortcut<>))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Assigns a stable registration sequence and returns the committed handler descriptor.
    /// </summary>
    /// <param name="descriptor">The handler descriptor discovered during module registration.</param>
    /// <returns>The descriptor annotated with its registration sequence.</returns>
    private IHandlerDescriptor CommitHandlerDescriptor(IHandlerDescriptor descriptor)
    {
        var sequence = _nextRegistrationSequence++;
        return HandlerDescriptorRegistration.WithRegistrationSequence(descriptor, sequence);
    }

    /// <summary>
    ///     Determines whether a message type belongs to the BCL <c>System</c> namespace and should be ignored.
    /// </summary>
    /// <param name="messageType">The candidate message type.</param>
    /// <returns>
    ///     <see langword="true" /> when the type is in <c>System</c> or <c>System.*</c>; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    private static bool IsSystemNamespace(Type messageType)
    {
        return messageType.Namespace is "System" ||
               messageType.Namespace?.StartsWith("System.", StringComparison.Ordinal) == true;
    }

    /// <summary>
    ///     Validates that a type carrying a pipeline marker also exposes a contract the pipeline can dispatch through.
    /// </summary>
    /// <param name="type">The registered type that produced no descriptor.</param>
    /// <param name="claimingBuilderCount">The number of descriptor builders that recognized the type.</param>
    /// <exception cref="PipelineContractException">
    ///     The type carries a pipeline marker but exposes no closed contract, so it would register successfully and
    ///     never run.
    /// </exception>
    /// <remarks>
    ///     Every pipeline marker is memberless, so a class can implement one without implementing any contract that
    ///     names a message type. Such a class produces no descriptor, and without this check it would fall through to
    ///     message-type registration and be silently accepted as a handler that never executes. Interfaces and abstract
    ///     classes are exempt because they are shapes rather than registrations.
    /// </remarks>
    private static void ThrowIfPipelineMarkerExposesNoContract(Type type, int claimingBuilderCount)
    {
        if (claimingBuilderCount == 0 || type.IsInterface || type.IsAbstract)
        {
            return;
        }

        throw new PipelineContractException(
            $"'{type.Name}' implements a LiteBus pipeline marker but exposes no contract that names a message type, so "
            + "nothing would ever dispatch to it. Implement a closed contract such as "
            + "IMessageGuard<TMessage>, IMessageShortcut<TMessage>, IMessagePreHandler<TMessage>, "
            + "IMessagePostHandler<TMessage, TMessageResult>, IMessageCompletionHandler<TMessage>, or "
            + "IMessageErrorHandler<TMessage>, or remove the marker.");
    }

    /// <summary>
    ///     Validates that an open generic handler exposes a type-parameter shape the registry can close.
    /// </summary>
    /// <param name="openGenericHandlerType">The open generic handler type definition.</param>
    /// <remarks>
    ///     One parameter binds the message type. Two are supported only when the handler implements a typed contract
    ///     over both, such as <c>IMessagePostHandler&lt;TMessage, TMessageResult&gt;</c>, because then the second
    ///     parameter has something to bind to: the result type the message declares. Two parameters where the second is
    ///     the handler's own invention, a <c>TContext</c> or a <c>TStore</c>, cannot be closed and are still rejected.
    /// </remarks>
    private static void ThrowIfOpenGenericHandlerShapeIsUnsupported(Type openGenericHandlerType)
    {
        var typeParams = openGenericHandlerType.GetGenericArguments();

        var supported = typeParams.Length switch
        {
            1 => true,
            2 => BindsBothParametersToATypedContract(openGenericHandlerType, typeParams),
            _ => false
        };

        if (!supported)
        {
            throw new UnsupportedOpenGenericHandlerException(openGenericHandlerType, typeParams.Length);
        }
    }

    /// <summary>
    ///     Determines whether an arity-2 handler implements a contract taking both of its type parameters, in order.
    /// </summary>
    /// <param name="openGenericHandlerType">The open generic handler type definition.</param>
    /// <param name="typeParams">The handler's own generic parameters.</param>
    /// <returns><see langword="true" /> when the second parameter binds to a result type rather than to nothing.</returns>
    /// <remarks>
    ///     Only the messaging-level contracts are listed. An axis contract such as
    ///     <c>ICommandPostHandler&lt;TCommand, TCommandResult&gt;</c> derives from one of them, and
    ///     <see cref="Type.GetInterfaces" /> returns the whole transitive closure, so matching here covers both.
    /// </remarks>
    private static bool BindsBothParametersToATypedContract(Type openGenericHandlerType, Type[] typeParams)
    {
        foreach (var contract in openGenericHandlerType.GetInterfaces())
        {
            if (!contract.IsGenericType || !TypedHandlerContracts.Contains(contract.GetGenericTypeDefinition()))
            {
                continue;
            }

            var arguments = contract.GetGenericArguments();

            if (arguments.Length == 2 && arguments[0] == typeParams[0] && arguments[1] == typeParams[1])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Resolves the result type a message declares through <see cref="IProducesResult{TMessageResult}" />.
    /// </summary>
    /// <param name="messageType">The concrete message type.</param>
    /// <returns>The declared result type, or <see langword="null" /> when the message declares none or declares several.</returns>
    /// <remarks>
    ///     Read from the message's own contract rather than from a main handler descriptor, so the answer does not
    ///     depend on whether the handler has been registered yet. A message declaring two result types is a modelling
    ///     error the axis contracts already make hard to write, and there is no correct arity-2 closing for it, so it
    ///     is answered as no result and the handler is skipped rather than closed over an arbitrary choice.
    /// </remarks>
    private static Type? FindDeclaredResultType(Type messageType)
    {
        Type? resultType = null;

        foreach (var contract in messageType.GetInterfaces())
        {
            if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(IProducesResult<>))
            {
                continue;
            }

            if (resultType is not null)
            {
                return null;
            }

            resultType = contract.GetGenericArguments()[0];
        }

        return resultType;
    }

    /// <summary>
    ///     Commits pending message descriptors to the main collection.
    /// </summary>
    private void CommitPendingMessages()
    {
        if (_pendingMessages.Count > 0)
        {
            foreach (var descriptor in _pendingMessages)
            {
                _descriptorsByType[descriptor.MessageType] = descriptor;
            }

            _committedMessages.AddRange(_pendingMessages);
            _pendingMessages.Clear();
            _pendingMessageTypes.Clear();
        }
    }
}
