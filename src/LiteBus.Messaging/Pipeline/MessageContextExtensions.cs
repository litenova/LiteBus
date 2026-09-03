using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Pipeline;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging;

/// <summary>
///     Provides extension methods for running the decision, pre-handler, post-handler, error, and completion stages of
///     the message handling pipeline.
/// </summary>
/// <remarks>
///     Mediation strategies compose the stages through these methods, so a custom strategy gets the same ordering,
///     answer, suppression, and completion guarantees as the strategies LiteBus ships.
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
    ///     The decision from the first guard or shortcut that ended the mediation, or <see cref="PipelineDecision.Continue" /> when
    ///     every stage let it proceed. Nothing after a decision runs.
    /// </returns>
    /// <remarks>
    ///     The stage order is not configurable and priority cannot reorder it. That is what makes "a shortcut never
    ///     answers a caller a guard would have denied" a guarantee rather than a convention: a global cache shortcut
    ///     cannot run ahead of a message-specific authorization guard, which the single pre-handler stage of earlier
    ///     versions could not express.
    /// </remarks>
    public static async Task<PipelineDecision> RunAsyncPreStages(
        this IMessageDependencies messageDependencies,
        object message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);

        var messageType = message.GetType();

        // The order is the order PreStage declares its members, so the declared order is the executed one rather
        // than something a hand-written call sequence has to keep in step.
        foreach (var stage in PipelineContracts.StagesInOrder)
        {
            if (!messageDependencies.HasPreStageHandlers(stage))
            {
                continue;
            }

            var startedAt = Stopwatch.GetTimestamp();
            using var stageActivity = MediationTelemetry.StartStage(stage, messageType);

            // Only a harness or a Try call installs a capture, so this is a dictionary miss on every ordinary
            // mediation. Read through the non-throwing accessor: this method is public, and a custom strategy that
            // has not opened an ambient scope must not start failing here.
            var capturing = AmbientExecutionContext.GetCurrentOrDefault();

            if (capturing is not null)
            {
                MediationEndingCapture.RecordStage(capturing, stage);
            }

            var decision = PipelineContracts.AggregationFor(stage) == StageAggregation.CollectFailures
                ? await messageDependencies.RunAsyncCollectingStage(stage, message, cancellationToken)
                    .ConfigureAwait(false)
                : await messageDependencies.RunStage(stage, message, cancellationToken).ConfigureAwait(false);

            MediationTelemetry.RecordStage(
                stage,
                messageType,
                Stopwatch.GetElapsedTime(startedAt),
                decision);

            if (decision.StopsPipeline)
            {
                return decision;
            }
        }

        return PipelineDecision.Continue;
    }

    /// <summary>
    ///     Runs the stages that decide whether a message may proceed, without running the stages that act on it.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating pre-stage handlers.</param>
    /// <param name="message">The message being evaluated.</param>
    /// <param name="cancellationToken">The cancellation token passed to each invocation.</param>
    /// <returns>
    ///     The decision from the first stage that would stop the message, or <see cref="PipelineDecision.Continue" />
    ///     when nothing objects.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This is what an <c>Evaluate</c> mediator method calls. It runs guards and then validators, in the same
    ///         fixed order the full pipeline runs them, and stops there.
    ///     </para>
    ///     <para>
    ///         The stages it skips are skipped because they act rather than decide. The shipped idempotency shortcut
    ///         claims a key, and a pre-handler exists to do work, so running either to answer "may I" would perform
    ///         part of the message the caller was only asking about.
    ///     </para>
    ///     <para>
    ///         It is a prefix of the fixed order rather than an arbitrary subset, which is what distinguishes it from
    ///         the per-stage helpers v7 removed. Those let a caller run one stage without the ones before it, which
    ///         cannot honor the guarantee that a shortcut never answers ahead of a guard.
    ///     </para>
    /// </remarks>
    public static async Task<PipelineDecision> RunAsyncDecisionStages(
        this IMessageDependencies messageDependencies,
        object message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);

        foreach (var stage in PipelineContracts.DecisionStagesInOrder)
        {
            if (!messageDependencies.HasPreStageHandlers(stage))
            {
                continue;
            }

            var decision = PipelineContracts.AggregationFor(stage) == StageAggregation.CollectFailures
                ? await messageDependencies.RunAsyncCollectingStage(stage, message, cancellationToken)
                    .ConfigureAwait(false)
                : await messageDependencies.RunStage(stage, message, cancellationToken).ConfigureAwait(false);

            if (decision.StopsPipeline)
            {
                return decision;
            }
        }

        return PipelineDecision.Continue;
    }

    /// <summary>
    ///     Runs every handler of one stage and gathers the failures they reported into a single decision.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating pre-stage handlers.</param>
    /// <param name="stage">The stage whose handlers should run.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token passed to each invocation.</param>
    /// <returns>
    ///     A decision carrying every failure the stage collected, or <see cref="PipelineDecision.Continue" /> when it found none.
    /// </returns>
    /// <remarks>
    ///     Validation is the only stage declaring <see cref="StageAggregation.CollectFailures" />, so the decision built
    ///     here reports <see cref="MediationOutcome.Invalid" />. A second collecting stage would have to decide what it
    ///     produces before reusing this.
    /// </remarks>
    private static async Task<PipelineDecision> RunAsyncCollectingStage(
        this IMessageDependencies messageDependencies,
        PreStage stage,
        object message,
        CancellationToken cancellationToken)
    {
        if (!messageDependencies.HasPreStageHandlers(stage))
        {
            return PipelineDecision.Continue;
        }

        List<ValidationFailure>? failures = null;

        foreach (var handler in messageDependencies.IndirectPreStageHandlers)
        {
            failures = await CollectFailuresAsync(handler, stage, message, failures, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var handler in messageDependencies.PreStageHandlers)
        {
            failures = await CollectFailuresAsync(handler, stage, message, failures, cancellationToken)
                .ConfigureAwait(false);
        }

        return failures is null ? PipelineDecision.Continue : PipelineDecision.Invalid(failures);
    }

    /// <summary>
    ///     Turns a refusal into the result the caller receives, or raises when no mapper covers the message.
    /// </summary>
    /// <typeparam name="TMessageResult">The type of result the message produces.</typeparam>
    /// <param name="messageDependencies">The message dependencies encapsulating the registered refusal mappers.</param>
    /// <param name="message">The message that was refused.</param>
    /// <param name="decision">The refusal produced by a guard or the validator stage.</param>
    /// <returns>The result the caller receives in place of the one the main handler would have produced.</returns>
    /// <exception cref="LiteBusMessageDeniedException">A guard denied and no mapper covers the message.</exception>
    /// <exception cref="LiteBusMessageInvalidException">
    ///     The validator stage reported failures and no mapper covers the message.
    /// </exception>
    /// <exception cref="PipelineContractException">
    ///     More than one mapper is registered at the same level of specificity, so which one applies would depend on
    ///     assembly scanning order.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="decision" /> is not a refusal. Only a denial and a validation failure reach a mapper.
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
        PipelineDecision decision)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(message);

        // A shortcut carries the result it answered with, so routing one through a mapper would replace a value the
        // caller was already owed. Checked here rather than deeper, where it would surface as a puzzling cast.
        if (!decision.IsRefusal)
        {
            throw new ArgumentException(
                $"A '{decision.Outcome}' decision is not a refusal, so no refusal mapper applies to it. Test "
                + $"{nameof(PipelineDecision.IsRefusal)} first, and read the result from "
                + $"{nameof(PipelineDecision.ResolveResult)} for an answered decision.",
                nameof(decision));
        }

        var mapper = SelectRefusalMapper<TMessageResult>(messageDependencies.RefusalMappers, message)
                     ?? SelectRefusalMapper<TMessageResult>(messageDependencies.IndirectRefusalMappers, message);

        if (mapper is null)
        {
            throw decision.CreateRefusalException(message.GetType());
        }

        var mapped = PipelineHandlerInvoker.InvokeRefusalMapper(
            mapper.Value.Handler.Value,
            mapper.Value.Descriptor,
            message,
            decision.ToRefusal());

        return (TMessageResult) mapped!;
    }

    /// <summary>
    ///     Selects the single refusal mapper in one collection that produces the result the caller expects.
    /// </summary>
    /// <typeparam name="TMessageResult">The type of result the message produces.</typeparam>
    /// <param name="mappers">The direct or indirect mappers registered for the message.</param>
    /// <param name="message">The message that was refused.</param>
    /// <returns>The mapper to invoke, or <see langword="null" /> when this collection holds none that applies.</returns>
    /// <exception cref="PipelineContractException">The collection holds more than one applicable mapper.</exception>
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
                throw new PipelineContractException(
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
    ///     Offers a fault to the registered error handlers, and rethrows it when none of them recovers.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating error handlers.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="messageResult">Whatever result existed when the fault happened, for handlers to inspect.</param>
    /// <param name="failure">The exception that ended the mediation.</param>
    /// <param name="executionContext">The execution context the mediation is running under.</param>
    /// <returns>
    ///     The error context the handlers saw, carrying whether one of them recovered and what it recovered with.
    /// </returns>
    /// <remarks>
    ///     The original stack is preserved through <see cref="ExceptionDispatchInfo" />, captured here so no caller has
    ///     to remember to. With no error handler registered, or with every handler leaving the outcome unhandled, the
    ///     fault is rethrown rather than being swallowed by the stage that observed it.
    /// </remarks>
    public static async Task<MessageErrorContext> RunAsyncErrorHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        object? messageResult,
        Exception failure,
        IExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        // Capturing here rather than at each call site is what stops a strategy rethrowing without the original stack.
        var exceptionDispatchInfo = ExceptionDispatchInfo.Capture(failure);

        using var scope = AmbientExecutionContext.CreateScope(executionContext);

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
            await PipelineHandlerInvoker
                .InvokeErrorHandlerAsync(errorHandler.Handler.Value, context, executionContext.CancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var errorHandler in messageDependencies.ErrorHandlers)
        {
            await PipelineHandlerInvoker
                .InvokeErrorHandlerAsync(errorHandler.Handler.Value, context, executionContext.CancellationToken)
                .ConfigureAwait(false);
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
    ///         Handlers run in ascending <see cref="HandlerPriorityAttribute" /> order regardless of whether they were
    ///         registered for the message type or for a base type it implements. The completion stage is the one role
    ///         that does not split the two, because the order here decides whether an application's unit of work commits
    ///         before or after LiteBus writes its audit record, and a split collection would put that outside the reach
    ///         of priority.
    ///     </para>
    ///     <para>
    ///         The stage is not cancellable. It observes an ending, and the ending has already happened, so handing it the
    ///         token that just fired would decision it recording exactly the cancellations and failures it exists to record.
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

        if (messageDependencies.CompletionHandlers.Count == 0)
        {
            return;
        }

        List<Exception>? suppressed = null;

        foreach (var completionHandler in messageDependencies.CompletionHandlers)
        {
            suppressed = await InvokeCompletionHandlerAsync(completionHandler, context, suppressed).ConfigureAwait(false);
        }

        if (suppressed is not null)
        {
            context.Exception!.Data[MediationExceptionData.SuppressedCompletionFaults] = suppressed;
        }
    }

    /// <summary>
    ///     Reports how a mediation ended to every registered completion handler.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating completion handlers.</param>
    /// <param name="message">The message that was mediated.</param>
    /// <param name="executionContext">The execution context the mediation ran under.</param>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="failure">The exception that ended the mediation, when one did.</param>
    /// <param name="reason">The reason a decision gave for stopping the pipeline, when one did.</param>
    /// <param name="code">The machine-readable code a decision gave for stopping the pipeline, when it supplied one.</param>
    /// <param name="messageResult">The result the main handler produced, when it ran and produced one.</param>
    /// <param name="duration">How long the mediation took.</param>
    /// <returns>A task that completes once every completion handler has run.</returns>
    /// <remarks>
    ///     A post-handler may have replaced what the caller receives through
    ///     <see cref="IExecutionContext.MessageResult" />, and the completion stage should see what the caller actually
    ///     got. That is resolved here rather than at each call site: getting it wrong reports the handler's own value to
    ///     an audit trail while the caller received a different one.
    /// </remarks>
    public static async Task RunAsyncCompletionHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        IExecutionContext executionContext,
        MediationOutcome outcome,
        Exception? failure,
        string? reason,
        string? code,
        object? messageResult,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        if (messageDependencies.CompletionHandlers.Count == 0)
        {
            return;
        }

        var context = new MessageCompletionContext
        {
            Message = message,
            Outcome = outcome,

            // A post-handler may have replaced what the caller receives, and the completion stage should see what the
            // caller actually got. Resolving it here is what stops each strategy having to remember.
            MessageResult = executionContext.MessageResult ?? messageResult,
            Exception = failure,
            Reason = reason,
            Code = code,
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
    ///     The decision from the first handler that ended the mediation, or <see cref="PipelineDecision.Continue" /> when every
    ///     handler in the stage let it proceed.
    /// </returns>
    /// <remarks>
    ///     Every pre-stage role shares one descriptor collection, ordered once by priority, so a stage is a filtered
    ///     pass over it rather than a separate collection. Indirect handlers run first, matching the existing rule that
    ///     a globally registered cross-cutting concern wraps a message-specific one. The validator stage does not use
    ///     this runner, because it collects failures instead of stopping at the first.
    /// </remarks>
    private static async Task<PipelineDecision> RunStage(
        this IMessageDependencies messageDependencies,
        PreStage stage,
        object message,
        CancellationToken cancellationToken)
    {
        // Most messages carry no guard or shortcut at all, and a stage that holds nothing should not cost an
        // enumerator over the shared descriptor collection to discover that.
        if (!messageDependencies.HasPreStageHandlers(stage))
        {
            return PipelineDecision.Continue;
        }

        foreach (var handler in messageDependencies.IndirectPreStageHandlers)
        {
            if (handler.Descriptor.Stage != stage)
            {
                continue;
            }

            var decision = await PipelineHandlerInvoker
                .InvokePreHandlerAsync(handler.Handler.Value, handler.Descriptor, message, cancellationToken)
                .ConfigureAwait(false);

            if (decision.StopsPipeline)
            {
                return decision;
            }
        }

        foreach (var handler in messageDependencies.PreStageHandlers)
        {
            if (handler.Descriptor.Stage != stage)
            {
                continue;
            }

            var decision = await PipelineHandlerInvoker
                .InvokePreHandlerAsync(handler.Handler.Value, handler.Descriptor, message, cancellationToken)
                .ConfigureAwait(false);

            if (decision.StopsPipeline)
            {
                return decision;
            }
        }

        return PipelineDecision.Continue;
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
    ///     Runs one handler when it belongs to the given stage, adding whatever it reported to the collected set.
    /// </summary>
    /// <param name="handler">The pre-stage handler being considered.</param>
    /// <param name="stage">The stage being collected for; a handler from any other stage is skipped.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="failures">The failures collected so far, or null when nothing has failed yet.</param>
    /// <param name="cancellationToken">The cancellation token passed to the validator invocation.</param>
    /// <returns>The collected failures, still null when the stage has found nothing wrong.</returns>
    /// <remarks>
    ///     The list is allocated only once something fails, so a message that validates cleanly costs nothing beyond the
    ///     stage filter.
    /// </remarks>
    private static async Task<List<ValidationFailure>?> CollectFailuresAsync(
        LazyHandler<IMessagePreStageHandler, IPreStageHandlerDescriptor> handler,
        PreStage stage,
        object message,
        List<ValidationFailure>? failures,
        CancellationToken cancellationToken)
    {
        if (handler.Descriptor.Stage != stage)
        {
            return failures;
        }

        var decision = await PipelineHandlerInvoker
            .InvokePreHandlerAsync(handler.Handler.Value, handler.Descriptor, message, cancellationToken)
            .ConfigureAwait(false);

        if (!decision.StopsPipeline)
        {
            return failures;
        }

        failures ??= [];
        failures.AddRange(decision.Failures);

        return failures;
    }
}
