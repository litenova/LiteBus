using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Provides extension methods for running the decision, pre-handler, post-handler, error, and completion stages of
///     the message handling pipeline.
/// </summary>
/// <remarks>
///     Mediation strategies compose the stages through these methods, so a custom strategy gets the same ordering,
///     short-circuit, suppression, and completion guarantees as the strategies LiteBus ships.
/// </remarks>
public static class MessageContextExtensions
{
    /// <summary>
    ///     Runs the three stages that precede the main handler, in the order the framework fixes: guards, then
    ///     shortcuts, then pre-handlers.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating pre-handlers.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token passed to each invocation.</param>
    /// <returns>
    ///     The stop from the first guard or shortcut that ended the mediation, or <see cref="PipelineStop.None" /> when
    ///     every stage let it proceed. Nothing after a stop runs.
    /// </returns>
    /// <remarks>
    ///     The stage order is not configurable and priority cannot reorder it. That is what makes "a shortcut never
    ///     answers a caller a guard would have refused" a guarantee rather than a convention: a global cache shortcut
    ///     cannot run ahead of a message-specific authorization guard, which the single pre-handler stage of earlier
    ///     versions could not express.
    /// </remarks>
    public static async Task<PipelineStop> RunAsyncPreStages(
        this IMessageDependencies messageDependencies,
        object message,
        CancellationToken cancellationToken)
    {
        var stop = await messageDependencies.RunAsyncGuards(message, cancellationToken).ConfigureAwait(false);

        if (stop.StopsPipeline)
        {
            return stop;
        }

        stop = await messageDependencies.RunAsyncValidators(message, cancellationToken).ConfigureAwait(false);

        if (stop.StopsPipeline)
        {
            return stop;
        }

        stop = await messageDependencies.RunAsyncShortcuts(message, cancellationToken).ConfigureAwait(false);

        if (stop.StopsPipeline)
        {
            return stop;
        }

        return await messageDependencies.RunAsyncPreHandlers(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs the guard stage until one guard refuses the message.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating pre-handlers.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token passed to each guard invocation.</param>
    /// <returns>
    ///     The stop from the first guard that refused the message, or <see cref="PipelineStop.None" /> when every guard
    ///     allowed it.
    /// </returns>
    public static Task<PipelineStop> RunAsyncGuards(
        this IMessageDependencies messageDependencies,
        object message,
        CancellationToken cancellationToken)
    {
        return messageDependencies.RunStage(PipelineStage.Guard, message, cancellationToken);
    }

    /// <summary>
    ///     Runs the validator stage, collecting the failures every validator reported.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating pre-stage handlers.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token passed to each validator invocation.</param>
    /// <returns>
    ///     A stop carrying every failure the stage collected, or <see cref="PipelineStop.None" /> when the message is
    ///     well-formed.
    /// </returns>
    /// <remarks>
    ///     Unlike the guard and shortcut stages, this one does not stop at the first decision. Every validator runs and
    ///     their failures are gathered into one stop, because a caller fixing a malformed message wants all of them at
    ///     once rather than one per round trip.
    /// </remarks>
    public static async Task<PipelineStop> RunAsyncValidators(
        this IMessageDependencies messageDependencies,
        object message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);

        if (!messageDependencies.HasPreStageHandlers(PipelineStage.Validator))
        {
            return PipelineStop.None;
        }

        List<ValidationFailure>? failures = null;

        foreach (var validator in messageDependencies.IndirectPreHandlers)
        {
            failures = await CollectValidationFailuresAsync(validator, message, failures, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var validator in messageDependencies.PreHandlers)
        {
            failures = await CollectValidationFailuresAsync(validator, message, failures, cancellationToken)
                .ConfigureAwait(false);
        }

        return failures is null ? PipelineStop.None : PipelineStop.Invalid(failures);
    }

    /// <summary>
    ///     Runs the shortcut stage until one shortcut answers the message.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating pre-handlers.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token passed to each shortcut invocation.</param>
    /// <returns>
    ///     The stop from the first shortcut that answered the message, or <see cref="PipelineStop.None" /> when none
    ///     did.
    /// </returns>
    public static Task<PipelineStop> RunAsyncShortcuts(
        this IMessageDependencies messageDependencies,
        object message,
        CancellationToken cancellationToken)
    {
        return messageDependencies.RunStage(PipelineStage.Shortcut, message, cancellationToken);
    }

    /// <summary>
    ///     Runs the pre-handler stage, where a message that is going to be handled is prepared.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating pre-handlers.</param>
    /// <param name="message">The message to be pre-handled.</param>
    /// <param name="cancellationToken">The cancellation token passed to each pre-handler invocation.</param>
    /// <returns>
    ///     Always <see cref="PipelineStop.None" />, because a pre-handler cannot stop the pipeline by returning.
    ///     Throwing ends the mediation as a fault rather than a decision.
    /// </returns>
    public static Task<PipelineStop> RunAsyncPreHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        CancellationToken cancellationToken)
    {
        return messageDependencies.RunStage(PipelineStage.PreHandler, message, cancellationToken);
    }

    /// <summary>
    ///     Turns a refusal into the result the caller receives, or raises when no mapper covers the message.
    /// </summary>
    /// <typeparam name="TMessageResult">The type of result the message produces.</typeparam>
    /// <param name="messageDependencies">The message dependencies encapsulating the registered refusal mappers.</param>
    /// <param name="message">The message that was refused.</param>
    /// <param name="stop">The refusal produced by a guard or the validator stage.</param>
    /// <returns>The result the caller receives in place of the one the main handler would have produced.</returns>
    /// <exception cref="LiteBusMessageDeniedException">A guard refused and no mapper covers the message.</exception>
    /// <exception cref="LiteBusMessageInvalidException">
    ///     The validator stage reported failures and no mapper covers the message.
    /// </exception>
    /// <exception cref="LiteBusConfigurationException">
    ///     More than one mapper is registered at the same level of specificity, so which one applies would depend on
    ///     assembly scanning order.
    /// </exception>
    /// <remarks>
    ///     A mapper registered for the concrete message type wins over one registered for a base type or interface,
    ///     matching how the rest of the pipeline resolves direct against indirect registrations. That is what lets an
    ///     application register one mapper for a whole axis and override it for a single message.
    /// </remarks>
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public static TMessageResult ResolveRefusalResult<TMessageResult>(
        this IMessageDependencies messageDependencies,
        object message,
        PipelineStop stop)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(message);

        var mapper = SelectRefusalMapper<TMessageResult>(messageDependencies.RefusalMappers, message)
                     ?? SelectRefusalMapper<TMessageResult>(messageDependencies.IndirectRefusalMappers, message);

        if (mapper is null)
        {
            throw stop.CreateRefusalException(message.GetType());
        }

        var mapped = PipelineHandlerInvoker.InvokeRefusalMapper(
            mapper.Value.Handler.Value,
            mapper.Value.Descriptor,
            message,
            stop.ToRefusal());

        return (TMessageResult) mapped!;
    }

    /// <summary>
    ///     Selects the single refusal mapper in one collection that produces the result the caller expects.
    /// </summary>
    /// <typeparam name="TMessageResult">The type of result the message produces.</typeparam>
    /// <param name="mappers">The direct or indirect mappers registered for the message.</param>
    /// <param name="message">The message that was refused.</param>
    /// <returns>The mapper to invoke, or <see langword="null" /> when this collection holds none that applies.</returns>
    /// <exception cref="LiteBusConfigurationException">The collection holds more than one applicable mapper.</exception>
    private static LazyHandler<IMessageRefusalMapper, IRefusalMapperDescriptor>? SelectRefusalMapper<TMessageResult>(
        ILazyHandlerCollection<IMessageRefusalMapper, IRefusalMapperDescriptor> mappers,
        object message)
    {
        LazyHandler<IMessageRefusalMapper, IRefusalMapperDescriptor>? selected = null;

        foreach (var mapper in mappers)
        {
            if (!mapper.Descriptor.MessageResultType.IsAssignableTo(typeof(TMessageResult)))
            {
                continue;
            }

            if (selected is not null)
            {
                throw new LiteBusConfigurationException(
                    $"'{message.GetType().Name}' has more than one refusal mapper producing "
                    + $"'{typeof(TMessageResult).Name}' registered at the same level: "
                    + $"'{selected.Value.Descriptor.HandlerType.Name}' and '{mapper.Descriptor.HandlerType.Name}'. "
                    + "Which one applied would depend on assembly scanning order, so remove one, or register the one "
                    + "that should win against the concrete message type so it takes precedence.");
            }

            selected = mapper;
        }

        return selected;
    }

    /// <summary>
    ///     Runs error handlers for a given context, allowing for centralized error handling logic to be applied in the case of
    ///     failures during the message handling process.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating error handlers.</param>
    /// <param name="message">The message that was being handled when the error occurred.</param>
    /// <param name="messageResult">The result of the message handling process, if any.</param>
    /// <param name="exceptionDispatchInfo">The exception that triggered the error handler.</param>
    /// <param name="cancellationToken">The cancellation token passed to each error handler invocation.</param>
    /// <returns>
    ///     The error context after all error handlers run. When <see cref="MessageErrorContext.Outcome" /> remains
    ///     <see cref="MessageErrorOutcome.Unhandled" />, the original exception is rethrown.
    /// </returns>
    public static async Task<MessageErrorContext> RunAsyncErrorHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        object? messageResult,
        ExceptionDispatchInfo exceptionDispatchInfo,
        CancellationToken cancellationToken)
    {
        if (messageDependencies.ErrorHandlers.Count + messageDependencies.IndirectErrorHandlers.Count == 0)
        {
            exceptionDispatchInfo.Throw();
        }

        var context = new MessageErrorContext
        {
            Message = message,
            Exception = exceptionDispatchInfo.SourceException,
            MessageResult = messageResult
        };

        foreach (var errorHandler in messageDependencies.IndirectErrorHandlers)
        {
            await InvokeErrorHandlerAsync(errorHandler.Handler.Value, context, cancellationToken).ConfigureAwait(false);
        }

        foreach (var errorHandler in messageDependencies.ErrorHandlers)
        {
            await InvokeErrorHandlerAsync(errorHandler.Handler.Value, context, cancellationToken).ConfigureAwait(false);
        }

        if (context.Outcome == MessageErrorOutcome.Unhandled)
        {
            exceptionDispatchInfo.Throw();
        }

        return context;
    }

    /// <summary>
    ///     Runs post-handlers for a given context, allowing for operations such as logging and further processing to be
    ///     performed after the primary message handling.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating post-handlers.</param>
    /// <param name="message">The message that has been handled.</param>
    /// <param name="messageResult">The result produced by the message handling process.</param>
    /// <param name="cancellationToken">The cancellation token passed to each post-handler invocation.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    /// <remarks>
    ///     Suppression is re-checked before each handler, so a post-handler that calls
    ///     <see cref="IExecutionContext.SuppressPostHandlers" /> stops the ones that have not run yet.
    /// </remarks>
    public static async Task RunAsyncPostHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        object? messageResult,
        CancellationToken cancellationToken)
    {
        var executionContext = AmbientExecutionContext.GetCurrentOrDefault();

        foreach (var postHandler in messageDependencies.PostHandlers)
        {
            if (executionContext?.PostHandlersSuppressed == true)
            {
                return;
            }

            await PipelineHandlerInvoker
                .InvokePostHandlerAsync(postHandler.Handler.Value, postHandler.Descriptor, message, messageResult, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var postHandler in messageDependencies.IndirectPostHandlers)
        {
            if (executionContext?.PostHandlersSuppressed == true)
            {
                return;
            }

            await PipelineHandlerInvoker
                .InvokePostHandlerAsync(postHandler.Handler.Value, postHandler.Descriptor, message, messageResult, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs completion handlers for a mediation that has ended, on every outcome path.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating completion handlers.</param>
    /// <param name="context">The completion context describing how the mediation ended.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    ///     <para>
    ///         Direct handlers run before indirect handlers, matching post-handler ordering, so that a globally registered
    ///         observer sees the message last.
    ///     </para>
    ///     <para>
    ///         The stage is not cancellable. It observes an ending, and the ending has already happened, so handing it the
    ///         token that just fired would stop it recording exactly the cancellations and failures it exists to record.
    ///         Handlers receive <see cref="CancellationToken.None" /> and apply their own deadline if they need one.
    ///     </para>
    ///     <para>
    ///         A fault raised by a completion handler propagates when the mediation was otherwise clean. When an
    ///         exception already ended the mediation, the fault is attached to it under
    ///         <see cref="MediationExceptionData.SuppressedCompletionFaults" /> rather than replacing it.
    ///     </para>
    /// </remarks>
    public static async Task RunAsyncCompletionHandlers(
        this IMessageDependencies messageDependencies,
        MessageCompletionContext context)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(context);

        if (messageDependencies.CompletionHandlers.Count + messageDependencies.IndirectCompletionHandlers.Count == 0)
        {
            return;
        }

        List<Exception>? suppressed = null;

        foreach (var completionHandler in messageDependencies.CompletionHandlers)
        {
            suppressed = await InvokeCompletionHandlerAsync(completionHandler, context, suppressed).ConfigureAwait(false);
        }

        foreach (var completionHandler in messageDependencies.IndirectCompletionHandlers)
        {
            suppressed = await InvokeCompletionHandlerAsync(completionHandler, context, suppressed).ConfigureAwait(false);
        }

        if (suppressed is not null)
        {
            context.Exception!.Data[MediationExceptionData.SuppressedCompletionFaults] = suppressed;
        }
    }

    /// <summary>
    ///     Builds a completion context and runs completion handlers inside the ambient execution scope.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating completion handlers.</param>
    /// <param name="message">The message that was mediated.</param>
    /// <param name="executionContext">The execution context used to scope the completion handlers.</param>
    /// <param name="outcome">The outcome describing how the mediation ended.</param>
    /// <param name="messageResult">The result observed before the mediation ended, when any.</param>
    /// <param name="exception">The exception that ended the mediation, when any.</param>
    /// <param name="reason">The reason a decision gave for stopping the pipeline, when one did.</param>
    /// <param name="duration">The elapsed mediation time.</param>
    /// <returns>A task representing the asynchronous completion stage.</returns>
    public static async Task RunAsyncCompletionHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        IExecutionContext executionContext,
        MessageOutcome outcome,
        object? messageResult,
        Exception? exception,
        string? reason,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        if (messageDependencies.CompletionHandlers.Count + messageDependencies.IndirectCompletionHandlers.Count == 0)
        {
            return;
        }

        var context = new MessageCompletionContext
        {
            Message = message,
            Outcome = outcome,
            MessageResult = messageResult,
            Exception = exception,
            Reason = reason,
            Duration = duration
        };

        using (AmbientExecutionContext.CreateScope(executionContext))
        {
            await messageDependencies.RunAsyncCompletionHandlers(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs the handlers of one decision stage, indirect before direct, until one of them stops the pipeline.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating pre-handlers.</param>
    /// <param name="stage">The stage whose handlers should run.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token passed to each invocation.</param>
    /// <returns>
    ///     The stop from the first handler that ended the mediation, or <see cref="PipelineStop.None" /> when every
    ///     handler in the stage let it proceed.
    /// </returns>
    /// <remarks>
    ///     Every pre-stage role shares one descriptor collection, ordered once by priority, so a stage is a filtered
    ///     pass over it rather than a separate collection. Indirect handlers run first, matching the existing rule that
    ///     a globally registered cross-cutting concern wraps a message-specific one. The validator stage does not use
    ///     this runner, because it collects failures instead of stopping at the first.
    /// </remarks>
    private static async Task<PipelineStop> RunStage(
        this IMessageDependencies messageDependencies,
        PipelineStage stage,
        object message,
        CancellationToken cancellationToken)
    {
        // Most messages carry no guard or shortcut at all, and a stage that holds nothing should not cost an
        // enumerator over the shared descriptor collection to discover that.
        if (!messageDependencies.HasPreStageHandlers(stage))
        {
            return PipelineStop.None;
        }

        foreach (var preHandler in messageDependencies.IndirectPreHandlers)
        {
            if (preHandler.Descriptor.Stage != stage)
            {
                continue;
            }

            var stop = await PipelineHandlerInvoker
                .InvokePreHandlerAsync(preHandler.Handler.Value, preHandler.Descriptor, message, cancellationToken)
                .ConfigureAwait(false);

            if (stop.StopsPipeline)
            {
                return stop;
            }
        }

        foreach (var preHandler in messageDependencies.PreHandlers)
        {
            if (preHandler.Descriptor.Stage != stage)
            {
                continue;
            }

            var stop = await PipelineHandlerInvoker
                .InvokePreHandlerAsync(preHandler.Handler.Value, preHandler.Descriptor, message, cancellationToken)
                .ConfigureAwait(false);

            if (stop.StopsPipeline)
            {
                return stop;
            }
        }

        return PipelineStop.None;
    }

    /// <summary>
    ///     Invokes one completion handler, collecting its fault when the mediation had already failed.
    /// </summary>
    /// <param name="completionHandler">The resolved completion handler and its descriptor.</param>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <param name="suppressed">The faults collected so far, or <see langword="null" /> when there are none.</param>
    /// <returns>The fault list, created on the first suppressed fault.</returns>
    private static async Task<List<Exception>?> InvokeCompletionHandlerAsync(
        LazyHandler<IMessageCompletionHandler, ICompletionHandlerDescriptor> completionHandler,
        MessageCompletionContext context,
        List<Exception>? suppressed)
    {
        try
        {
            await PipelineHandlerInvoker.InvokeCompletionHandlerAsync(
                    completionHandler.Handler.Value,
                    completionHandler.Descriptor,
                    context,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A completion handler observes the outcome and must never replace the original fault.
        catch (Exception exception) when (context.Exception is not null)
#pragma warning restore CA1031
        {
            (suppressed ??= []).Add(exception);
        }

        return suppressed;
    }

    /// <summary>
    ///     Invokes an error handler using an explicit cancellation token when an asynchronous method is available.
    /// </summary>
    /// <param name="handler">The error handler instance.</param>
    /// <param name="context">The error context observed during mediation.</param>
    /// <param name="cancellationToken">The cancellation token for the invocation.</param>
    /// <returns>A task representing the asynchronous error handler operation.</returns>
    private static Task InvokeErrorHandlerAsync(
        IMessageErrorHandler handler,
        MessageErrorContext context,
        CancellationToken cancellationToken)
    {
        return PipelineHandlerInvocation.InvokeErrorHandlerAsync(handler, context, cancellationToken);
    }

    /// <summary>
    ///     Runs one handler when it belongs to the validator stage, adding whatever it reported to the collected set.
    /// </summary>
    /// <param name="handler">The pre-stage handler being considered.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="failures">The failures collected so far, or null when nothing has failed yet.</param>
    /// <param name="cancellationToken">The cancellation token passed to the validator invocation.</param>
    /// <returns>The collected failures, still null when the stage has found nothing wrong.</returns>
    /// <remarks>
    ///     The list is allocated only once something fails, so a message that validates cleanly costs nothing beyond the
    ///     stage filter.
    /// </remarks>
    private static async Task<List<ValidationFailure>?> CollectValidationFailuresAsync(
        LazyHandler<IMessagePreStageHandler, IPreHandlerDescriptor> handler,
        object message,
        List<ValidationFailure>? failures,
        CancellationToken cancellationToken)
    {
        if (handler.Descriptor.Stage != PipelineStage.Validator)
        {
            return failures;
        }

        var stop = await PipelineHandlerInvoker
            .InvokePreHandlerAsync(handler.Handler.Value, handler.Descriptor, message, cancellationToken)
            .ConfigureAwait(false);

        if (!stop.StopsPipeline)
        {
            return failures;
        }

        failures ??= [];
        failures.AddRange(stop.Failures);

        return failures;
    }
}
