using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Invokes pre-handlers and post-handlers through the contract they were registered under.
/// </summary>
/// <remarks>
///     <para>
///         The pipeline cannot call these stages through a default interface method on their non-generic contract,
///         because a class that implements the contract for more than one message type would then have no most-specific
///         implementation and would not compile. Handlers legitimately do that, so the non-generic contracts are markers
///         and the closed contract is selected here instead.
///     </para>
///     <para>
///         Which closed contract to use is not guessed. It comes from the handler descriptor built during registration,
///         so a handler registered for a base type is invoked through that base type, and a class implementing several
///         contracts is invoked through the right one. An invoker delegate is built once per handler and contract pair
///         and cached, so dispatch is a dictionary lookup and a delegate call rather than a reflective invoke.
///     </para>
/// </remarks>
internal static class PipelineHandlerInvoker
{
    /// <summary>
    ///     Cached pre-handler invokers, keyed by handler runtime type and registered message type.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type Handler, Type Message),
        Func<IMessagePreHandler, object, CancellationToken, Task<PipelineDirective>>> PreInvokers = new();

    /// <summary>
    ///     Cached post-handler invokers, keyed by registered message type and result type.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type Message, Type Result),
        Func<IMessagePostHandler, object, object?, CancellationToken, Task>> PostInvokers = new();

    /// <summary>
    ///     Runs a pre-handler through the contract it was registered under.
    /// </summary>
    /// <param name="handler">The pre-handler instance.</param>
    /// <param name="descriptor">The descriptor that recorded the contract at registration.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The directive that tells the pipeline whether to continue.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public static Task<PipelineDirective> InvokePreHandlerAsync(
        IMessagePreHandler handler,
        IPreHandlerDescriptor descriptor,
        object message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(descriptor);

        var messageType = ResolveClosedType(descriptor.MessageType, message.GetType());
        var invoker = PreInvokers.GetOrAdd((handler.GetType(), messageType), BuildPreInvoker);
        return invoker(handler, message, cancellationToken);
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

        var messageType = ResolveClosedType(descriptor.MessageType, message.GetType());
        var resultType = ResolveClosedType(descriptor.MessageResultType, messageResult?.GetType() ?? typeof(object));
        var invoker = PostInvokers.GetOrAdd((messageType, resultType), BuildPostInvoker);
        return invoker(handler, message, messageResult, cancellationToken);
    }

    /// <summary>
    ///     Resolves the closed type to dispatch through.
    /// </summary>
    /// <param name="declared">The type recorded on the handler descriptor at registration.</param>
    /// <param name="runtime">The runtime type observed during mediation.</param>
    /// <returns>The declared type when it is closed, otherwise the runtime type.</returns>
    /// <remarks>
    ///     A generic message is registered under its generic type definition, so the descriptor records an open type
    ///     that cannot close a generic method. In that case the runtime type is the correct closed contract, because an
    ///     open generic handler was closed over exactly that type during registration.
    /// </remarks>
    private static Type ResolveClosedType(Type declared, Type runtime)
    {
        return declared.ContainsGenericParameters ? runtime : declared;
    }

    /// <summary>
    ///     Builds the invoker for one pre-handler type and registered message type.
    /// </summary>
    /// <param name="key">The handler runtime type and registered message type.</param>
    /// <returns>The cached invoker delegate.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    private static Func<IMessagePreHandler, object, CancellationToken, Task<PipelineDirective>> BuildPreInvoker(
        (Type Handler, Type Message) key)
    {
        var shortCircuiting = typeof(IShortCircuitingPreHandler<>).MakeGenericType(key.Message);

        var methodName = shortCircuiting.IsAssignableFrom(key.Handler)
            ? nameof(InvokeShortCircuitingPreHandler)
            : nameof(InvokePlainPreHandler);

        return typeof(PipelineHandlerInvoker)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(key.Message)
            .CreateDelegate<Func<IMessagePreHandler, object, CancellationToken, Task<PipelineDirective>>>();
    }

    /// <summary>
    ///     Builds the invoker for one registered message and result type pair.
    /// </summary>
    /// <param name="key">The registered message type and result type.</param>
    /// <returns>The cached invoker delegate.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    private static Func<IMessagePostHandler, object, object?, CancellationToken, Task> BuildPostInvoker(
        (Type Message, Type Result) key)
    {
        return typeof(PipelineHandlerInvoker)
            .GetMethod(nameof(InvokePostHandler), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(key.Message, key.Result)
            .CreateDelegate<Func<IMessagePostHandler, object, object?, CancellationToken, Task>>();
    }

    /// <summary>
    ///     Runs a pre-handler that cannot short-circuit, and reports that the pipeline continues.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <param name="handler">The pre-handler instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>Always <see cref="PipelineDirective.Continue" />.</returns>
    private static async Task<PipelineDirective> InvokePlainPreHandler<TMessage>(
        IMessagePreHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        await ((IMessagePreHandler<TMessage>) handler)
            .PreHandleAsync((TMessage) message, cancellationToken)
            .ConfigureAwait(false);

        return PipelineDirective.Continue;
    }

    /// <summary>
    ///     Runs a pre-handler that may short-circuit, and returns its decision.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <param name="handler">The pre-handler instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The directive returned by the pre-handler.</returns>
    private static Task<PipelineDirective> InvokeShortCircuitingPreHandler<TMessage>(
        IMessagePreHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        return ((IShortCircuitingPreHandler<TMessage>) handler)
            .PreHandleAsync((TMessage) message, cancellationToken);
    }

    /// <summary>
    ///     Runs a post-handler through its closed contract.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <typeparam name="TMessageResult">The registered result type.</typeparam>
    /// <param name="handler">The post-handler instance.</param>
    /// <param name="message">The message that was handled.</param>
    /// <param name="messageResult">The result produced by the main handler, when any.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous post-handling operation.</returns>
    private static Task InvokePostHandler<TMessage, TMessageResult>(
        IMessagePostHandler handler,
        object message,
        object? messageResult,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        return ((IMessagePostHandler<TMessage, TMessageResult>) handler)
            .PostHandleAsync((TMessage) message, (TMessageResult?) messageResult, cancellationToken);
    }
}
