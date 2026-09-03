using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging;

/// <summary>
///     Resolves the <see cref="PipelineDispatch" /> for one handler descriptor and invokes the handler through it.
/// </summary>
/// <remarks>
///     <para>
///         A descriptor whose contract was closed at registration already carries its dispatch, so invocation is a field
///         read and a delegate call. Only a handler registered for a generic message arrives here without one, because
///         its contract is open until the runtime message type is known. Those are bound on first dispatch and cached.
///     </para>
/// </remarks>
internal static class PipelineHandlerInvoker
{
    /// <summary>
    ///     Dispatches bound at runtime for handlers registered under an open generic contract, keyed by closed contract.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PipelineDispatch> RuntimeDispatches = new();

    /// <summary>
    ///     Runs a pre-handler, guard, or shortcut through the contract it was registered under.
    /// </summary>
    /// <param name="handler">The pre-handler instance.</param>
    /// <param name="descriptor">The descriptor that recorded the contract at registration.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The decision that tells the pipeline whether to continue.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public static Task<PipelineDecision> InvokePreHandlerAsync(
        IMessagePreStageHandler handler,
        IPreStageHandlerDescriptor descriptor,
        object message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(descriptor);

        var dispatch = descriptor.Dispatch ?? ResolveRuntimeDispatch(descriptor, message.GetType());
        return dispatch.InvokePreHandlerAsync(handler, message, cancellationToken);
    }

    /// <summary>
    ///     Runs a post-handler through the contract it was registered under.
    /// </summary>
    /// <param name="handler">The post-handler instance.</param>
    /// <param name="descriptor">The descriptor that recorded the contract at registration.</param>
    /// <param name="message">The message that was handled.</param>
    /// <param name="messageResult">The result produced by the main handler, when any.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous post-handling operation.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public static Task InvokePostHandlerAsync(
        IMessagePostHandler handler,
        IPostHandlerDescriptor descriptor,
        object message,
        object? messageResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(descriptor);

        var dispatch = descriptor.Dispatch ?? ResolveRuntimeDispatch(descriptor, message.GetType());
        return dispatch.InvokePostHandlerAsync(handler, message, messageResult, cancellationToken);
    }

    /// <summary>
    ///     Runs a completion handler through the contract it was registered under.
    /// </summary>
    /// <param name="handler">The completion handler instance.</param>
    /// <param name="descriptor">The descriptor that recorded the contract at registration.</param>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <param name="cancellationToken">The cancellation token passed to the completion stage.</param>
    /// <returns>A task representing the asynchronous completion-handling operation.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public static Task InvokeCompletionHandlerAsync(
        IMessageCompletionHandler handler,
        ICompletionHandlerDescriptor descriptor,
        MessageCompletionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(context);

        var dispatch = descriptor.Dispatch ?? ResolveRuntimeDispatch(descriptor, context.Message.GetType());
        return dispatch.InvokeCompletionHandlerAsync(handler, context, cancellationToken);
    }

    /// <summary>
    ///     Runs an error handler with the cancellation token the mediation was given.
    /// </summary>
    /// <param name="handler">The error handler instance.</param>
    /// <param name="context">The error context observed during mediation.</param>
    /// <param name="cancellationToken">The cancellation token for the error handler invocation.</param>
    /// <returns>A task representing the asynchronous error handler operation.</returns>
    /// <remarks>
    ///     An error handler takes a context object rather than the message, so its contract has no generic parameters
    ///     to close and it needs no dispatch. It is invoked here anyway so that every pipeline stage is reached through
    ///     one type.
    /// </remarks>
    public static Task InvokeErrorHandlerAsync(
        IMessageErrorHandler handler,
        MessageErrorContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(context);

        return handler.HandleErrorAsync(context, cancellationToken);
    }

    /// <summary>
    ///     Invokes a refusal mapper through the closed contract recorded in its descriptor.
    /// </summary>
    /// <param name="mapper">The mapper resolved for the message.</param>
    /// <param name="descriptor">The descriptor the mapper was registered under.</param>
    /// <param name="message">The message that was refused.</param>
    /// <param name="refusal">The outcome, reason, and code the decision supplied.</param>
    /// <returns>The result the caller receives in place of the one the main handler would have produced.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public static object? InvokeRefusalMapper(
        IMessageRefusalMapper mapper,
        IRefusalMapperDescriptor descriptor,
        object message,
        Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(descriptor);

        var dispatch = descriptor.Dispatch ?? ResolveRuntimeDispatch(descriptor, message.GetType());

        return dispatch.InvokeRefusalMapper(mapper, message, refusal);
    }

    /// <summary>
    ///     Binds and caches the dispatch for a handler registered under an open generic contract.
    /// </summary>
    /// <param name="descriptor">The descriptor whose contract is open.</param>
    /// <param name="runtimeMessageType">The concrete runtime type of the message being mediated.</param>
    /// <returns>The dispatch for the contract closed over the runtime message type.</returns>
    /// <exception cref="PipelineContractException">
    ///     Thrown when the recorded contract cannot be closed over the runtime message type.
    /// </exception>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    private static PipelineDispatch ResolveRuntimeDispatch(IHandlerDescriptor descriptor, Type runtimeMessageType)
    {
        var closedContract = CloseOverRuntimeMessage(descriptor.ContractType, runtimeMessageType);

        if (RuntimeDispatches.TryGetValue(closedContract, out var cached))
        {
            return cached;
        }

        var dispatch = PipelineDispatch.For(closedContract)
                       ?? throw new PipelineContractException(
                           $"The handler '{descriptor.HandlerType.Name}' was registered under the contract "
                           + $"'{descriptor.ContractType.Name}', which the pipeline cannot dispatch for message "
                           + $"'{runtimeMessageType.Name}'.");

        RuntimeDispatches[closedContract] = dispatch;
        return dispatch;
    }

    /// <summary>
    ///     Closes an open generic handler contract over the runtime message type.
    /// </summary>
    /// <param name="openContract">The contract recorded at registration, which contains generic parameters.</param>
    /// <param name="runtimeMessageType">The concrete runtime type of the message being mediated.</param>
    /// <returns>The closed contract to dispatch through.</returns>
    /// <remarks>
    ///     An open generic handler exposes exactly one type parameter, which the registry enforces, so every open
    ///     argument on the contract is closed by the type arguments of the runtime message type.
    /// </remarks>
    private static Type CloseOverRuntimeMessage(Type openContract, Type runtimeMessageType)
    {
        if (!openContract.ContainsGenericParameters)
        {
            return openContract;
        }

        var messageArguments = runtimeMessageType.IsGenericType
            ? runtimeMessageType.GetGenericArguments()
            : [];

        var contractArguments = openContract.GetGenericArguments();
        var closedArguments = new Type[contractArguments.Length];

        for (var index = 0; index < contractArguments.Length; index++)
        {
            closedArguments[index] = CloseArgument(contractArguments[index], runtimeMessageType, messageArguments);
        }

        return openContract.GetGenericTypeDefinition().MakeGenericType(closedArguments);
    }

    /// <summary>
    ///     Closes one contract type argument over the runtime message type.
    /// </summary>
    /// <param name="argument">The contract type argument, which may contain generic parameters.</param>
    /// <param name="runtimeMessageType">The concrete runtime type of the message being mediated.</param>
    /// <param name="messageArguments">The type arguments of the runtime message type.</param>
    /// <returns>The closed type argument.</returns>
    private static Type CloseArgument(Type argument, Type runtimeMessageType, Type[] messageArguments)
    {
        if (!argument.ContainsGenericParameters)
        {
            return argument;
        }

        if (argument.IsGenericParameter)
        {
            return messageArguments.Length > 0 ? messageArguments[0] : runtimeMessageType;
        }

        return messageArguments.Length > 0
            ? argument.GetGenericTypeDefinition().MakeGenericType(messageArguments)
            : runtimeMessageType;
    }
}
