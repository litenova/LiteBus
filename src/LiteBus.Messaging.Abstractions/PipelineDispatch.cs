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
///         The contract also decides which stage runs the handler, which <see cref="StageFor" /> reads. Guards,
///         shortcuts, and plain pre-handlers share one descriptor kind and one marker contract, so that stage is what
///         keeps every guard ahead of every shortcut regardless of priority or registration scope.
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
    ///     The bound invoker for a pre-stage contract, when this dispatch describes one.
    /// </summary>
    private readonly Func<IMessagePreStageHandler, object, CancellationToken, Task<PipelineStop>>? _preHandler;

    /// <summary>
    ///     The bound invoker for a refusal mapper contract, when this dispatch describes one.
    /// </summary>
    private readonly Func<IMessageRefusalMapper, object, Refusal, object?>? _refusalMapper;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelineDispatch" /> class.
    /// </summary>
    /// <param name="contractType">The closed contract this dispatch invokes.</param>
    /// <param name="preHandler">The bound pre-handler invoker, when the contract is a pre-handler contract.</param>
    /// <param name="postHandler">The bound post-handler invoker, when the contract is a post-handler contract.</param>
    /// <param name="completionHandler">
    ///     The bound completion handler invoker, when the contract is a completion handler contract.
    /// </param>
    /// <param name="refusalMapper">The bound mapper invoker, when the contract is a refusal mapper contract.</param>
    private PipelineDispatch(
        Type contractType,
        Func<IMessagePreStageHandler, object, CancellationToken, Task<PipelineStop>>? preHandler,
        Func<IMessagePostHandler, object, object?, CancellationToken, Task>? postHandler,
        Func<IMessageCompletionHandler, MessageCompletionContext, CancellationToken, Task>? completionHandler,
        Func<IMessageRefusalMapper, object, Refusal, object?>? refusalMapper = null)
    {
        ContractType = contractType;
        _preHandler = preHandler;
        _postHandler = postHandler;
        _completionHandler = completionHandler;
        _refusalMapper = refusalMapper;
    }

    /// <summary>
    ///     Gets the closed contract this dispatch invokes.
    /// </summary>
    public Type ContractType { get; }

    /// <summary>
    ///     Reads which stage of the pre stage runs a handler registered under the given contract.
    /// </summary>
    /// <param name="contractType">The pre-stage contract discovered on the handler during registration.</param>
    /// <returns>
    ///     <see cref="PipelineStage.Guard" /> for a guard contract, <see cref="PipelineStage.Validator" /> for a
    ///     validator contract, <see cref="PipelineStage.Shortcut" /> for a shortcut contract, and
    ///     <see cref="PipelineStage.PreHandler" /> for anything else.
    /// </returns>
    /// <remarks>
    ///     The contract answers this whether or not it is closed, so a handler registered for a generic message is
    ///     assigned its stage at registration even though its dispatch cannot be bound until the runtime message type is
    ///     known. Keeping the mapping here means the stage and the invoker are read off the same switch.
    /// </remarks>
    public static PipelineStage StageFor(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        if (!contractType.IsGenericType)
        {
            return PipelineStage.PreHandler;
        }

        var definition = contractType.GetGenericTypeDefinition();

        if (definition == typeof(IMessageGuard<>))
        {
            return PipelineStage.Guard;
        }

        if (definition == typeof(IMessageValidator<>))
        {
            return PipelineStage.Validator;
        }

        if (definition == typeof(IMessageShortcut<>) || definition == typeof(IMessageShortcut<,>))
        {
            return PipelineStage.Shortcut;
        }

        return PipelineStage.PreHandler;
    }

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
            return ForPreHandler(contractType, nameof(InvokePreHandler), arguments);
        }

        if (definition == typeof(IMessageGuard<>))
        {
            return ForPreHandler(contractType, nameof(InvokeGuard), arguments);
        }

        if (definition == typeof(IMessageValidator<>))
        {
            return ForPreHandler(contractType, nameof(InvokeValidator), arguments);
        }

        if (definition == typeof(IMessageShortcut<>))
        {
            return ForPreHandler(contractType, nameof(InvokeShortcut), arguments);
        }

        if (definition == typeof(IMessageShortcut<,>))
        {
            return ForPreHandler(contractType, nameof(InvokeTypedShortcut), arguments);
        }

        if (definition == typeof(IMessageRefusalMapper<,>))
        {
            return new PipelineDispatch(contractType, null, null, null, BindRefusalMapper(arguments));
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
    ///     Builds the dispatch for one pre-handler, guard, or shortcut contract.
    /// </summary>
    /// <param name="contractType">The closed contract discovered on the handler during registration.</param>
    /// <param name="methodName">The name of the static invoker to bind.</param>
    /// <param name="arguments">The closed type arguments of the contract.</param>
    /// <returns>The dispatch for the contract.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    private static PipelineDispatch ForPreHandler(Type contractType, string methodName, Type[] arguments)
    {
        return new PipelineDispatch(contractType, BindPreHandler(methodName, arguments), null, null);
    }

    /// <summary>
    ///     Invokes the pre-handler, guard, or shortcut this dispatch was built for.
    /// </summary>
    /// <param name="handler">The handler instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The stop that tells the pipeline whether to continue.</returns>
    internal Task<PipelineStop> InvokePreHandlerAsync(
        IMessagePreStageHandler handler,
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
    private static Func<IMessagePreStageHandler, object, CancellationToken, Task<PipelineStop>> BindPreHandler(
        string methodName,
        Type[] arguments)
    {
        return Bind<Func<IMessagePreStageHandler, object, CancellationToken, Task<PipelineStop>>>(methodName, arguments);
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
    /// <returns>Always <see cref="PipelineStop.None" />.</returns>
    private static async Task<PipelineStop> InvokePreHandler<TMessage>(
        IMessagePreStageHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        await ((IMessagePreHandler<TMessage>) handler)
            .PreHandleAsync((TMessage) message, cancellationToken)
            .ConfigureAwait(false);

        return PipelineStop.None;
    }

    /// <summary>
    ///     Runs a guard and returns its verdict as the stop the pipeline acts on.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <param name="handler">The guard instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The stop for a refusal, or <see cref="PipelineStop.None" /> when the message may proceed.</returns>
    private static async Task<PipelineStop> InvokeGuard<TMessage>(
        IMessagePreStageHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        var verdict = await ((IMessageGuard<TMessage>) handler)
            .DecideAsync((TMessage) message, cancellationToken)
            .ConfigureAwait(false);

        return verdict.ToStop();
    }

    /// <summary>
    ///     Runs a validator and returns what it reported as the stop the stage runner collects from.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <param name="handler">The validator instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>
    ///     A stop carrying this validator's failures, or <see cref="PipelineStop.None" /> when it found nothing wrong.
    ///     The stage runner gathers these rather than acting on the first, so returning a stop here does not by itself
    ///     end the mediation.
    /// </returns>
    private static async Task<PipelineStop> InvokeValidator<TMessage>(
        IMessagePreStageHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        var validity = await ((IMessageValidator<TMessage>) handler)
            .ValidateAsync((TMessage) message, cancellationToken)
            .ConfigureAwait(false);

        return validity.ToStop();
    }

    /// <summary>
    ///     Runs a shortcut over a message that produces no result, and returns its answer as the stop the pipeline acts
    ///     on.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <param name="handler">The shortcut instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The stop for an answer, or <see cref="PipelineStop.None" /> when the mediation proceeds.</returns>
    private static async Task<PipelineStop> InvokeShortcut<TMessage>(
        IMessagePreStageHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        var shortcut = await ((IMessageShortcut<TMessage>) handler)
            .TryAnswerAsync((TMessage) message, cancellationToken)
            .ConfigureAwait(false);

        return shortcut.ToStop();
    }

    /// <summary>
    ///     Runs a shortcut over a message that produces a result, and returns its answer as the stop the pipeline acts
    ///     on.
    /// </summary>
    /// <typeparam name="TMessage">The registered message type.</typeparam>
    /// <typeparam name="TMessageResult">The registered result type.</typeparam>
    /// <param name="handler">The shortcut instance.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The stop for an answer, or <see cref="PipelineStop.None" /> when the mediation proceeds.</returns>
    private static async Task<PipelineStop> InvokeTypedShortcut<TMessage, TMessageResult>(
        IMessagePreStageHandler handler,
        object message,
        CancellationToken cancellationToken)
        where TMessage : notnull
    {
        var shortcut = await ((IMessageShortcut<TMessage, TMessageResult>) handler)
            .TryAnswerAsync((TMessage) message, cancellationToken)
            .ConfigureAwait(false);

        return shortcut.ToStop();
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

    /// <summary>
    ///     Invokes the refusal mapper this dispatch describes.
    /// </summary>
    /// <param name="mapper">The mapper resolved for the message.</param>
    /// <param name="message">The message that was refused.</param>
    /// <param name="refusal">The outcome, reason, and code the decision supplied.</param>
    /// <returns>The result the caller receives in place of the one the main handler would have produced.</returns>
    internal object? InvokeRefusalMapper(IMessageRefusalMapper mapper, object message, Refusal refusal)
    {
        return _refusalMapper!(mapper, message, refusal);
    }

    /// <summary>
    ///     Binds the invoker for a closed refusal mapper contract.
    /// </summary>
    /// <param name="arguments">The message and result types the contract is closed over.</param>
    /// <returns>A delegate that invokes the mapper through its closed contract.</returns>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    private static Func<IMessageRefusalMapper, object, Refusal, object?> BindRefusalMapper(Type[] arguments)
    {
        return Bind<Func<IMessageRefusalMapper, object, Refusal, object?>>(nameof(InvokeRefusalMapperCore), arguments);
    }

    /// <summary>
    ///     Invokes a refusal mapper through its closed contract.
    /// </summary>
    /// <typeparam name="TMessage">The message type the mapper covers.</typeparam>
    /// <typeparam name="TMessageResult">The result type the mapper produces.</typeparam>
    /// <param name="mapper">The mapper resolved for the message.</param>
    /// <param name="message">The message that was refused.</param>
    /// <param name="refusal">The outcome, reason, and code the decision supplied.</param>
    /// <returns>The result the caller receives.</returns>
    private static object? InvokeRefusalMapperCore<TMessage, TMessageResult>(
        IMessageRefusalMapper mapper,
        object message,
        Refusal refusal)
        where TMessage : notnull
    {
        return ((IMessageRefusalMapper<TMessage, TMessageResult>) mapper).Map((TMessage) message, refusal);
    }
}
