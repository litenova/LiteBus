using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

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
    ///     Declarative metadata resolved for <see cref="MessageType" />.
    /// </summary>
    private readonly MessageMetadata _metadata;

    /// <summary>
    ///     Direct completion handlers registered for <see cref="MessageType" />.
    /// </summary>
    private readonly List<ICompletionHandlerDescriptor> _completionHandlers = [];

    /// <summary>
    ///     Completion handlers registered for a base type or interface of <see cref="MessageType" />.
    /// </summary>
    private readonly List<ICompletionHandlerDescriptor> _indirectCompletionHandlers = [];

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
    private readonly List<IPreStageHandlerDescriptor> _indirectPreHandlers = [];

    /// <summary>
    ///     Direct post-handlers registered for <see cref="MessageType" />.
    /// </summary>
    private readonly List<IPostHandlerDescriptor> _postHandlers = [];

    /// <summary>
    ///     Direct pre-handlers registered for <see cref="MessageType" />.
    /// </summary>
    private readonly List<IPreStageHandlerDescriptor> _preHandlers = [];

    /// <summary>
    ///     Refusal mappers registered for this exact message type.
    /// </summary>
    private readonly List<IRefusalMapperDescriptor> _refusalMappers = [];

    /// <summary>
    ///     Refusal mappers registered for a base type or interface of this message type.
    /// </summary>
    private readonly List<IRefusalMapperDescriptor> _indirectRefusalMappers = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageDescriptor" /> class.
    /// </summary>
    /// <param name="messageType">The message type represented by this descriptor.</param>
    public MessageDescriptor(Type messageType)
    {
        MessageType = messageType;
        IsGeneric = messageType.IsGenericType;
        _metadata = new MessageMetadata(messageType);

        // Only attributes that declare themselves as metadata are collected, and each is converted to the value type a
        // definition would contribute, so a definition for the same message overwrites it instead of sitting beside it.
        List<DeclarationExemption>? exemptions = null;

        foreach (var attribute in messageType.GetCustomAttributes(inherit: true))
        {
            switch (attribute)
            {
                case IMessageDeclarationSource declaration:
                    ThrowIfAnnotationDisagrees(declaration);
                    _metadata.Set(
                        declaration.DeclarationType,
                        declaration.CreateDeclaration(),
                        messageType,
                        MetadataSourceKind.Attribute);
                    break;

                // Exemptions are collected rather than set one at a time. Metadata holds one value per key type, and a
                // message may be exempt from several requirements, so the attributes have to collapse into one set
                // instead of overwriting each other.
                case DeclarationExemptAttribute exempt:
                    (exemptions ??= []).Add(exempt.CreateExemption());
                    break;
            }
        }

        if (exemptions is not null)
        {
            _metadata.Set(
                typeof(DeclarationExemptions),
                new DeclarationExemptions(exemptions),
                messageType,
                MetadataSourceKind.Attribute);
        }
    }

    /// <inheritdoc />
    public IMessageMetadata Metadata => _metadata;

    /// <summary>
    ///     Verifies that a declaring attribute's <see cref="MessageDeclarationAttribute" /> names the same value type
    ///     its <see cref="IMessageDeclarationSource.DeclarationType" /> returns.
    /// </summary>
    /// <param name="declaration">The declaring attribute instance found on a message type.</param>
    /// <exception cref="LiteBusConfigurationException">The annotation and the property disagree.</exception>
    /// <remarks>
    ///     The annotation is what an analyzer reads, and the property is what the registry reads. Letting them drift
    ///     would make LB1020 report a message as undeclared while registration accepts it, or the reverse, which is
    ///     worse than either rule being absent.
    /// </remarks>
    private static void ThrowIfAnnotationDisagrees(IMessageDeclarationSource declaration)
    {
        var annotation = declaration.GetType().GetCustomAttribute<MessageDeclarationAttribute>(inherit: false);

        if (annotation is null || annotation.DeclarationType == declaration.DeclarationType)
        {
            return;
        }

        throw new LiteBusConfigurationException(
            $"The attribute '{declaration.GetType().Name}' is annotated [MessageDeclaration(typeof("
            + $"{annotation.DeclarationType.Name}))] but its DeclarationType returns '{declaration.DeclarationType.Name}'. "
            + "The annotation is what analyzers read and the property is what registration reads, so they have to name "
            + "the same type.");
    }

    /// <summary>
    ///     Applies a value declared by a message definition to this descriptor's metadata.
    /// </summary>
    /// <param name="keyType">The metadata key type the definition declared.</param>
    /// <param name="value">The metadata value the definition declared.</param>
    /// <param name="declaringMessageType">The message type the definition was written for.</param>
    public void ApplyMetadata(Type keyType, object value, Type declaringMessageType)
    {
        _metadata.Set(keyType, value, declaringMessageType, MetadataSourceKind.Definition);
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
    public IReadOnlyCollection<IPreStageHandlerDescriptor> PreStageHandlers => _preHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IPreStageHandlerDescriptor> IndirectPreStageHandlers => _indirectPreHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IErrorHandlerDescriptor> ErrorHandlers => _errorHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IErrorHandlerDescriptor> IndirectErrorHandlers => _indirectErrorHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<ICompletionHandlerDescriptor> CompletionHandlers => _completionHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<ICompletionHandlerDescriptor> IndirectCompletionHandlers => _indirectCompletionHandlers;

    /// <inheritdoc />
    public IReadOnlyCollection<IRefusalMapperDescriptor> RefusalMappers => _refusalMappers;

    /// <inheritdoc />
    public IReadOnlyCollection<IRefusalMapperDescriptor> IndirectRefusalMappers => _indirectRefusalMappers;

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
                case IRefusalMapperDescriptor refusalMapperDescriptor:
                    _refusalMappers.Add(refusalMapperDescriptor);
                    break;

                case ICompletionHandlerDescriptor completionHandlerDescriptor:
                    _completionHandlers.Add(completionHandlerDescriptor);
                    break;
                case IErrorHandlerDescriptor errorHandlerDescriptor:
                    _errorHandlers.Add(errorHandlerDescriptor);
                    break;
                case IMainHandlerDescriptor mainHandlerDescriptor:
                    ThrowIfUntypedShortcutMeetsResult(mainHandlerDescriptor, _preHandlers);
                    _handlers.Add(mainHandlerDescriptor);
                    break;
                case IPostHandlerDescriptor postHandlerDescriptor:
                    _postHandlers.Add(postHandlerDescriptor);
                    break;
                case IPreStageHandlerDescriptor preHandlerDescriptor:
                    ThrowIfUntypedShortcutMeetsResult(preHandlerDescriptor, _handlers);
                    _preHandlers.Add(preHandlerDescriptor);
                    break;
            }
        }
        else if (MessageType.IsAssignableTo(descriptor.MessageType))
        {
            switch (descriptor)
            {
                case IRefusalMapperDescriptor refusalMapperDescriptor:
                    _indirectRefusalMappers.Add(refusalMapperDescriptor);
                    break;

                case ICompletionHandlerDescriptor completionHandlerDescriptor:
                    _indirectCompletionHandlers.Add(completionHandlerDescriptor);
                    break;
                case IErrorHandlerDescriptor errorHandlerDescriptor:
                    _indirectErrorHandlers.Add(errorHandlerDescriptor);
                    break;
                case IMainHandlerDescriptor mainHandlerDescriptor:
                    _indirectHandlers.Add(mainHandlerDescriptor);
                    break;
                case IPostHandlerDescriptor postHandlerDescriptor:
                    _indirectPostHandlers.Add(postHandlerDescriptor);
                    break;
                case IPreStageHandlerDescriptor preHandlerDescriptor:
                    _indirectPreHandlers.Add(preHandlerDescriptor);
                    break;
            }
        }
        else if (ImplementsOpenGeneric(descriptor.MessageType, MessageType))
        {
            switch (descriptor)
            {
                case IRefusalMapperDescriptor refusalMapperDescriptor:
                    _indirectRefusalMappers.Add(refusalMapperDescriptor);
                    break;

                case ICompletionHandlerDescriptor completionHandlerDescriptor:
                    _indirectCompletionHandlers.Add(completionHandlerDescriptor);
                    break;
                case IErrorHandlerDescriptor errorHandlerDescriptor:
                    _indirectErrorHandlers.Add(errorHandlerDescriptor);
                    break;
                case IMainHandlerDescriptor mainHandlerDescriptor:
                    _indirectHandlers.Add(mainHandlerDescriptor);
                    break;
                case IPostHandlerDescriptor postHandlerDescriptor:
                    _indirectPostHandlers.Add(postHandlerDescriptor);
                    break;
                case IPreStageHandlerDescriptor preHandlerDescriptor:
                    _indirectPreHandlers.Add(preHandlerDescriptor);
                    break;
            }
        }
    }

    /// <summary>
    ///     The untyped shortcut contract, which answers without carrying a result.
    /// </summary>
    private static readonly Type UntypedShortcutContract = typeof(IMessageShortcut<>);

    /// <summary>
    ///     Rejects a main handler that produces a result when an untyped shortcut is already registered for this exact
    ///     message.
    /// </summary>
    /// <param name="mainHandler">The main handler being linked.</param>
    /// <param name="preStageHandlers">The pre-stage handlers already linked directly to this message.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     An untyped shortcut is registered for a message that produces a result.
    /// </exception>
    private void ThrowIfUntypedShortcutMeetsResult(
        IMainHandlerDescriptor mainHandler,
        List<IPreStageHandlerDescriptor> preStageHandlers)
    {
        if (!ProducesResult(mainHandler.MessageResultType))
        {
            return;
        }

        foreach (var preStageHandler in preStageHandlers)
        {
            if (IsUntypedShortcut(preStageHandler))
            {
                throw UntypedShortcutOnResultMessage(preStageHandler, mainHandler.MessageResultType);
            }
        }
    }

    /// <summary>
    ///     Rejects an untyped shortcut registered for this exact message when the message produces a result.
    /// </summary>
    /// <param name="preStageHandler">The pre-stage handler being linked.</param>
    /// <param name="mainHandlers">The main handlers already linked directly to this message.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     An untyped shortcut is registered for a message that produces a result.
    /// </exception>
    /// <remarks>
    ///     Both directions are checked because a handler may be registered before or after the message it handles, and
    ///     the registry commits after every call rather than in one pass at the end.
    /// </remarks>
    private void ThrowIfUntypedShortcutMeetsResult(
        IPreStageHandlerDescriptor preStageHandler,
        List<IMainHandlerDescriptor> mainHandlers)
    {
        if (!IsUntypedShortcut(preStageHandler))
        {
            return;
        }

        foreach (var mainHandler in mainHandlers)
        {
            if (ProducesResult(mainHandler.MessageResultType))
            {
                throw UntypedShortcutOnResultMessage(preStageHandler, mainHandler.MessageResultType);
            }
        }
    }

    /// <summary>
    ///     Determines whether a descriptor was discovered from the untyped shortcut contract.
    /// </summary>
    /// <param name="descriptor">The pre-stage descriptor to test.</param>
    /// <returns><see langword="true" /> when the handler answers without carrying a result.</returns>
    private static bool IsUntypedShortcut(IPreStageHandlerDescriptor descriptor)
    {
        return descriptor.Stage == PreStage.Shortcut
               && descriptor.ContractType.IsGenericType
               && descriptor.ContractType.GetGenericTypeDefinition() == UntypedShortcutContract;
    }

    /// <summary>
    ///     Determines whether a main handler hands the caller a value.
    /// </summary>
    /// <param name="messageResultType">The result type recorded on the main handler descriptor.</param>
    /// <returns><see langword="true" /> when the message produces something a shortcut would have to supply.</returns>
    /// <remarks>
    ///     A handler that produces nothing closes <c>IMessageHandler&lt;TMessage, TMessageResult&gt;</c> over
    ///     <see cref="Task" />. Every other closing carries a value, whether it is a <see cref="Task{TResult}" /> or the
    ///     <c>IAsyncEnumerable</c> of a stream query.
    /// </remarks>
    private static bool ProducesResult(Type messageResultType)
    {
        return messageResultType != typeof(Task);
    }

    /// <summary>
    ///     Builds the error reported when an untyped shortcut is registered for a message that produces a result.
    /// </summary>
    /// <param name="shortcut">The offending shortcut descriptor.</param>
    /// <param name="messageResultType">The result type the main handler produces.</param>
    /// <returns>The exception to raise.</returns>
    /// <remarks>
    ///     Worded to match analyzer LB1019, which reports the same mistake at compile time. The analyzer is a warning
    ///     and is absent from a project that does not reference the analyzer package, so registration is where the
    ///     guarantee actually lives.
    /// </remarks>
    private LiteBusConfigurationException UntypedShortcutOnResultMessage(
        IPreStageHandlerDescriptor shortcut,
        Type messageResultType)
    {
        var resultName = UnwrapResultType(messageResultType).Name;

        return new LiteBusConfigurationException(
            $"Shortcut '{shortcut.HandlerType.Name}' implements the untyped shortcut contract for "
            + $"'{MessageType.Name}', which produces '{resultName}'. The untyped answer cannot carry a result, so "
            + $"answering would fail at dispatch. Implement IMessageShortcut<{MessageType.Name}, {resultName}> "
            + "instead, or the axis contract that matches it.");
    }

    /// <summary>
    ///     Unwraps the value a handler hands back from the task or sequence that carries it.
    /// </summary>
    /// <param name="messageResultType">The result type recorded on the main handler descriptor.</param>
    /// <returns>The type a shortcut would have to supply.</returns>
    private static Type UnwrapResultType(Type messageResultType)
    {
        return messageResultType.IsGenericType
               && messageResultType.GetGenericTypeDefinition() == typeof(Task<>)
            ? messageResultType.GetGenericArguments()[0]
            : messageResultType;
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

        if (messageType.IsGenericType && messageType.GetGenericTypeDefinition() == openGenericType)
        {
            return true;
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
