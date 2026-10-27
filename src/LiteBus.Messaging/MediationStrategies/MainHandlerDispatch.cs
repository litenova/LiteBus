using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Pipeline;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Invokes a main handler through the closed contract it was registered under.
/// </summary>
/// <remarks>
///     <para>
///         A strategy's message type parameter is the compile-time type of the mediated message, which is not always the
///         type a handler was registered for. <c>IEventMediator.PublishAsync(IEvent, ...)</c> erases the event type to
///         <c>IEvent</c>, and a base-typed variable erases it the same way through the generic overloads. Handler contracts
///         are contravariant, so a handler registered for the concrete runtime type does not satisfy a type test against the
///         erased type.
///     </para>
///     <para>
///         The handler descriptor records the closed contract the handler implements for the message it was registered
///         for, so the handler is called through that contract rather than a non-generic entry point on the handler. That
///         is what lets one class implement several closed handler contracts: each registration carries its own contract,
///         and no handler has to route an untyped message itself.
///     </para>
/// </remarks>
internal static class MainHandlerDispatch
{
    /// <summary>
    ///     Holds one bound invoker per closed main handler contract for the lifetime of the process.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<IMessageHandler, object, CancellationToken, object>> Invokers = new();

    /// <summary>
    ///     Invokes the handler through the closed contract recorded on its descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor the handler was resolved from.</param>
    /// <param name="handler">The resolved main handler instance.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">The cancellation token for the handler invocation.</param>
    /// <returns>
    ///     What the contract's handle method returns: a <see cref="Task" />, a <see cref="Task{TResult}" />, an
    ///     <see cref="IAsyncEnumerable{T}" />, or the result of a synchronous contract.
    /// </returns>
    /// <exception cref="PipelineContractException">
    ///     The descriptor's contract is not an <see cref="IMessageHandler{TMessage,TMessageResult}" /> contract.
    /// </exception>
    /// <remarks>
    ///     An open generic handler is recorded under its open contract, such as
    ///     <c>IMessageHandler&lt;ProductViewed&lt;T&gt;, Task&gt;</c>. That contract is closed over the runtime message type
    ///     first, the same way pipeline stages are, so the invoker matches the closed handler instance.
    /// </remarks>
    [RequiresUnreferencedCode("Main handler dispatch closes handler contracts over registered message types.")]
    public static object Invoke(
        IMainHandlerDescriptor descriptor,
        IMessageHandler handler,
        object message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(message);

        var contractType = descriptor.ContractType.ContainsGenericParameters
            ? PipelineHandlerInvoker.CloseOverRuntimeMessage(descriptor.ContractType, message.GetType())
            : descriptor.ContractType;

        var invoker = Invokers.GetOrAdd(contractType, static closedContract => Bind(closedContract));
        return invoker(handler, message, cancellationToken);
    }

    /// <summary>
    ///     Binds the invoker that matches the shape of one closed main handler contract.
    /// </summary>
    /// <param name="contractType">The closed <see cref="IMessageHandler{TMessage,TMessageResult}" /> contract.</param>
    /// <returns>The bound invoker.</returns>
    /// <exception cref="PipelineContractException">The contract is not a closed main handler contract.</exception>
    [RequiresUnreferencedCode("Main handler dispatch closes handler contracts over registered message types.")]
    private static Func<IMessageHandler, object, CancellationToken, object> Bind(Type contractType)
    {
        if (!contractType.IsGenericType ||
            contractType.ContainsGenericParameters ||
            contractType.GetGenericTypeDefinition() != typeof(IMessageHandler<,>))
        {
            throw new PipelineContractException(
                $"'{contractType.Name}' is not a closed IMessageHandler<TMessage, TMessageResult> contract, so the mediator "
                + "cannot invoke the main handler registered under it.");
        }

        var arguments = contractType.GetGenericArguments();
        var messageType = arguments[0];
        var resultType = arguments[1];

        if (resultType == typeof(Task))
        {
            return BindInvoker(nameof(InvokeAsync), messageType);
        }

        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return BindInvoker(nameof(InvokeAsyncWithResult), messageType, resultType.GetGenericArguments()[0]);
        }

        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
        {
            return BindInvoker(nameof(InvokeStream), messageType, resultType.GetGenericArguments()[0]);
        }

        return BindInvoker(nameof(InvokeSynchronous), messageType, resultType);
    }

    /// <summary>
    ///     Closes one of the typed invokers over the supplied type arguments and creates its delegate.
    /// </summary>
    /// <param name="methodName">The name of the typed invoker.</param>
    /// <param name="typeArguments">The type arguments to close the invoker over.</param>
    /// <returns>The bound delegate.</returns>
    [RequiresUnreferencedCode("Main handler dispatch closes handler contracts over registered message types.")]
    private static Func<IMessageHandler, object, CancellationToken, object> BindInvoker(
        string methodName,
        params Type[] typeArguments)
    {
        return typeof(MainHandlerDispatch)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeArguments)
            .CreateDelegate<Func<IMessageHandler, object, CancellationToken, object>>();
    }

    /// <summary>
    ///     Invokes a handler registered under <c>IMessageHandler&lt;TMessage, Task&gt;</c>.
    /// </summary>
    /// <typeparam name="TMessage">The message type the handler was registered for.</typeparam>
    /// <param name="handler">The handler instance.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">The cancellation token passed to an asynchronous handler.</param>
    /// <returns>The task the handler returned.</returns>
    [SuppressMessage("Performance", "CA1859", Justification = "Bound by reflection into one delegate shape shared by every contract.")]
    private static object InvokeAsync<TMessage>(IMessageHandler handler, object message, CancellationToken cancellationToken)
        where TMessage : notnull
    {
        if (handler is IAsyncMessageHandler<TMessage> asyncHandler)
        {
            return asyncHandler.HandleAsync((TMessage) message, cancellationToken);
        }

        return ((IMessageHandler<TMessage, Task>) handler).Handle((TMessage) message);
    }

    /// <summary>
    ///     Invokes a handler registered under <c>IMessageHandler&lt;TMessage, Task&lt;TMessageResult&gt;&gt;</c>.
    /// </summary>
    /// <typeparam name="TMessage">The message type the handler was registered for.</typeparam>
    /// <typeparam name="TMessageResult">The result type the handler produces.</typeparam>
    /// <param name="handler">The handler instance.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">The cancellation token passed to an asynchronous handler.</param>
    /// <returns>The task the handler returned.</returns>
    [SuppressMessage("Performance", "CA1859", Justification = "Bound by reflection into one delegate shape shared by every contract.")]
    private static object InvokeAsyncWithResult<TMessage, TMessageResult>(
        IMessageHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        if (handler is IAsyncMessageHandler<TMessage, TMessageResult> asyncHandler)
        {
            return asyncHandler.HandleAsync((TMessage) message, cancellationToken);
        }

        return ((IMessageHandler<TMessage, Task<TMessageResult>>) handler).Handle((TMessage) message);
    }

    /// <summary>
    ///     Invokes a handler registered under <c>IMessageHandler&lt;TMessage, IAsyncEnumerable&lt;TMessageResult&gt;&gt;</c>.
    /// </summary>
    /// <typeparam name="TMessage">The message type the handler was registered for.</typeparam>
    /// <typeparam name="TMessageResult">The streamed item type.</typeparam>
    /// <param name="handler">The handler instance.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">The cancellation token passed to a stream handler.</param>
    /// <returns>The stream the handler returned.</returns>
    [SuppressMessage("Performance", "CA1859", Justification = "Bound by reflection into one delegate shape shared by every contract.")]
    private static object InvokeStream<TMessage, TMessageResult>(
        IMessageHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        if (handler is IStreamMessageHandler<TMessage, TMessageResult> streamHandler)
        {
            return streamHandler.StreamAsync((TMessage) message, cancellationToken);
        }

        return ((IMessageHandler<TMessage, IAsyncEnumerable<TMessageResult>>) handler).Handle((TMessage) message);
    }

    /// <summary>
    ///     Invokes a handler registered under a synchronous <c>IMessageHandler&lt;TMessage, TMessageResult&gt;</c>.
    /// </summary>
    /// <typeparam name="TMessage">The message type the handler was registered for.</typeparam>
    /// <typeparam name="TMessageResult">The result type the handler produces.</typeparam>
    /// <param name="handler">The handler instance.</param>
    /// <param name="message">The message to pass to the handler.</param>
    /// <param name="cancellationToken">Unused; a synchronous contract takes no cancellation token.</param>
    /// <returns>The handler's result.</returns>
    private static object InvokeSynchronous<TMessage, TMessageResult>(
        IMessageHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
        where TMessageResult : notnull
    {
        return ((IMessageHandler<TMessage, TMessageResult>) handler).Handle((TMessage) message);
    }
}
