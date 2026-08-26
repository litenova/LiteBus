using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Holds the delegate that invokes one handler through the closed contract it was registered under.
/// </summary>
/// <remarks>
///     <para>
///         The pipeline cannot call a pre-handler, post-handler, or completion handler through a default interface method
///         on its non-generic contract, because a class that implements the contract for more than one message type would
///         then have no most-specific implementation and would not compile. Handlers legitimately do that, so the
///         non-generic contracts are markers and the closed contract is selected here instead.
///     </para>
///     <para>
///         Which closed contract to use is not guessed. It is the contract the handler descriptor recorded during
///         registration, so a handler registered for a base type is invoked through that base type, and a class
///         implementing several contracts is invoked through the right one. The delegate is built once while the
///         descriptor is being built, which keeps reflection in the registration path and reduces dispatch to a field
///         read and a delegate call.
///     </para>
///     <para>
///         This type is a framework hook exposed for descriptor construction. Applications do not use it.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PipelineDispatch
{
    /// <summary>
    ///     The bound invoker for a completion handler contract, when this dispatch describes one.
    /// </summary>
    private readonly Func<IMessageCompletionHandler, MessageCompletionContext, CancellationToken, Task>? _completionHandler;

    /// <summary>
    ///     The bound invoker for a post-handler contract, when this dispatch describes one.
    /// </summary>
    private readonly Func<IMessagePostHandler, object, object?, CancellationToken, Task>? _postHandler;

    /// <summary>
    ///     The bound invoker for a pre-handler or gate contract, when this dispatch describes one.
    /// </summary>
    private readonly Func<IMessagePreHandler, object, CancellationToken, Task<PipelineDirective>>? _preHandler;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelineDispatch" /> class.
    /// </summary>
    /// <param name="contractType">The closed contract this dispatch invokes.</param>
    /// <param name="preHandler">The bound pre-handler invoker, when the contract is a pre-handler contract.</param>
    /// <param name="postHandler">The bound post-handler invoker, when the contract is a post-handler contract.</param>
    /// <param name="completionHandler">
    ///     The bound completion handler invoker, when the contract is a completion handler contract.
    /// </param>
    private PipelineDispatch(
        Type contractType,
        Func<IMessagePreHandler, object, CancellationToken, Task<PipelineDirective>>? preHandler,
        Func<IMessagePostHandler, object, object?, CancellationToken, Task>? postHandler,
        Func<IMessageCompletionHandler, MessageCompletionContext, CancellationToken, Task>? completionHandler)
    {
        ContractType = contractType;
        _preHandler = preHandler;
        _postHandler = postHandler;
        _completionHandler = completionHandler;
    }

    /// <summary>
    ///     Gets the closed contract this dispatch invokes.
    /// </summary>
    public Type ContractType { get; }

    /// <summary>
    ///     Builds the dispatch for one closed handler contract.
    /// </summary>
    /// <param name="contractType">The closed generic contract discovered on the handler during registration.</param>
    /// <returns>
    ///     The dispatch for the contract, or <see langword="null" /> when the contract still contains generic parameters
    ///     or is not a stage the pipeline dispatches this way.
    /// </returns>
    /// <remarks>
    ///     A generic message is registered under its generic type definition, so its contract is open at registration and
    ///     cannot be bound here. Those handlers are bound on first dispatch instead, against the runtime message type.
    /// </remarks>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public static PipelineDispatch? For(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        if (!contractType.IsGenericType || contractType.ContainsGenericParameters)
        {
            return null;
        }

        var definition = contractType.GetGenericTypeDefinition();
        var arguments = contractType.GetGenericArguments();

        if (definition == typeof(IMessagePreHandler<>))
        {
            return new PipelineDispatch(contractType, BindPreHandler(nameof(InvokePreHandler), arguments), null, null);
        }

        if (definition == typeof(IMessageGate<>))
        {
            return new PipelineDispatch(contractType, BindPreHandler(nameof(InvokeGate), arguments), null, null);
        }

        if (definition == typeof(IMessageGate<,>))
        {
            return new PipelineDispatch(contractType, BindPreHandler(nameof(InvokeTypedGate), arguments), null, null);
        }

        if (definition == typeof(IMessagePostHandler<,>))
        {
            return new PipelineDispatch(contractType, null, BindPostHandler(arguments), null);
        }

        if (definition == typeof(IMessageCompletionHandler<>))
        {
            return new PipelineDispatch(contractType, null, null, BindCompletionHandler(nameof(InvokeCompletionHandler), arguments));
        }

        if (definition == typeof(IMessageCompletionHandler<,>))
        {
            return new PipelineDispatch(contractType, null, null, BindCompletionHandler(nameof(InvokeTypedCompletionHandler), arguments));
        }

        return null;
    }

    /// <summary>
    ///     Invokes the pre-handler or gate this dispatch was built for.
    /// </summary>
    /// <param name="handler">The handler instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The directive that tells the pipeline whether to continue.</returns>
    internal Task<PipelineDirective> InvokePreHandlerAsync(
        IMessagePreHandler handler,
        object message,
        CancellationToken cancellationToken)
    {
        return _preHandler!(handler, message, cancellationToken);
    }

    /// <summary>
    ///     Invokes the post-handler this dispatch was built for.
    /// </summary>
    /// <param name="handler">The handler instance.</param>
    /// <param name="message">The message that was handled.</param>
    /// <param name="messageResult">The result produced by the main handler, when any.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous post-handling operation.</returns>
    internal Task InvokePostHandlerAsync(
        IMessagePostHandler handler,
        object message,
        object? messageResult,
        CancellationToken cancellationToken)
    {
        return _postHandler!(handler, message, messageResult, cancellationToken);
    }

    /// <summary>
    ///     Invokes the completion handler this dispatch was built for.
    /// </summary>
    /// <param name="handler">The handler instance.</param>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <param name="cancellationToken">The cancellation token passed to the completion stage.</param>
    /// <returns>A task representing the asynchronous completion-handling operation.</returns>
    internal Task InvokeCompletionHandlerAsync(
        IMessageCompletionHandler handler,
        MessageCompletionContext context,
        CancellationToken cancellationToken)
    {
        return _completionHandler!(handler, context, cancellationToken);
    }

    /// <summary>
    ///     Binds one of the static pre-handler invokers over the contract's type arguments.
    /// </summary>
    /// <param name="methodName">The name of the static invoker to bind.</param>
    /// <param name="arguments">The closed type arguments of the contract.</param>
    /// <returns>The bound delegate.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    private static Func<IMessagePreHandler, object, CancellationToken, Task<PipelineDirective>> BindPreHandler(
        string methodName,
        Type[] arguments)
    {
        return Bind<Func<IMessagePreHandler, object, CancellationToken, Task<PipelineDirective>>>(methodName, arguments);
    }

    /// <summary>
    ///     Binds the static post-handler invoker over the contract's type arguments.
    /// </summary>
    /// <param name="arguments">The closed type arguments of the contract.</param>
    /// <returns>The bound delegate.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    private static Func<IMessagePostHandler, object, object?, CancellationToken, Task> BindPostHandler(Type[] arguments)
    {
        return Bind<Func<IMessagePostHandler, object, object?, CancellationToken, Task>>(nameof(InvokePostHandler), arguments);
    }

    /// <summary>
    ///     Binds one of the static completion handler invokers over the contract's type arguments.
    /// </summary>
    /// <param name="methodName">The name of the static invoker to bind.</param>
    /// <param name="arguments">The closed type arguments of the contract.</param>
    /// <returns>The bound delegate.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    private static Func<IMessageCompletionHandler, MessageCompletionContext, CancellationToken, Task> BindCompletionHandler(
        string methodName,
        Type[] arguments)
    {
        return Bind<Func<IMessageCompletionHandler, MessageCompletionContext, CancellationToken, Task>>(methodName, arguments);
    }

    /// <summary>
    ///     Closes one of the static invokers over the supplied type arguments and creates its delegate.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type the invoker matches.</typeparam>
    /// <param name="methodName">The name of the static invoker to bind.</param>
    /// <param name="arguments">The closed type arguments of the contract.</param>
    /// <returns>The bound delegate.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    private static TDelegate Bind<TDelegate>(string methodName, Type[] arguments)
        where TDelegate : Delegate
    {
        return typeof(PipelineDispatch)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(arguments)
            .CreateDelegate<TDelegate>();
    }

    /// <summary>
    ///     Runs a pre-handler that cannot stop the pipeline, and reports that the pipeline continues.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <param name="handler">The pre-handler instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>Always <see cref="PipelineDirective.Continue" />.</returns>
    private static async Task<PipelineDirective> InvokePreHandler<TMessage>(
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
    ///     Runs a gate over a message that produces no result, and returns its decision.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <param name="handler">The gate instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The directive returned by the gate.</returns>
    private static Task<PipelineDirective> InvokeGate<TMessage>(
        IMessagePreHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        return ((IMessageGate<TMessage>) handler).DecideAsync((TMessage) message, cancellationToken);
    }

    /// <summary>
    ///     Runs a gate over a message that produces a result, and returns its decision.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <typeparam name="TMessageResult">The registered result type.</typeparam>
    /// <param name="handler">The gate instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The directive returned by the gate, converted to the untyped shape the pipeline acts on.</returns>
    private static async Task<PipelineDirective> InvokeTypedGate<TMessage, TMessageResult>(
        IMessagePreHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        var directive = await ((IMessageGate<TMessage, TMessageResult>) handler)
            .DecideAsync((TMessage) message, cancellationToken)
            .ConfigureAwait(false);

        return directive.AsUntyped();
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

    /// <summary>
    ///     Runs a completion handler through its closed contract.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <param name="handler">The completion handler instance.</param>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <param name="cancellationToken">The cancellation token passed to the completion stage.</param>
    /// <returns>A task representing the asynchronous completion-handling operation.</returns>
    private static Task InvokeCompletionHandler<TMessage>(
        IMessageCompletionHandler handler,
        MessageCompletionContext context,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        return ((IMessageCompletionHandler<TMessage>) handler)
            .HandleCompletionAsync(context.AsTyped<TMessage>(), cancellationToken);
    }

    /// <summary>
    ///     Runs a completion handler that expects a typed result through its closed contract.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <typeparam name="TMessageResult">The registered result type.</typeparam>
    /// <param name="handler">The completion handler instance.</param>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <param name="cancellationToken">The cancellation token passed to the completion stage.</param>
    /// <returns>A task representing the asynchronous completion-handling operation.</returns>
    private static Task InvokeTypedCompletionHandler<TMessage, TMessageResult>(
        IMessageCompletionHandler handler,
        MessageCompletionContext context,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        return ((IMessageCompletionHandler<TMessage, TMessageResult>) handler)
            .HandleCompletionAsync(context.AsTyped<TMessage, TMessageResult>(), cancellationToken);
    }
}
