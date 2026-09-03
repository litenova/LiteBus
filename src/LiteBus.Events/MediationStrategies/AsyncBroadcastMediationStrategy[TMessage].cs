using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Pipeline;

namespace LiteBus.Events.MediationStrategies;

/// <summary>
///     Implements a publish-subscribe message mediation strategy that broadcasts a message to multiple handlers.
///     This strategy orchestrates the full execution pipeline, including pre-handlers, main handlers, post-handlers,
///     and error handlers, while respecting configured concurrency settings.
/// </summary>
/// <typeparam name="TMessage">The type of the message to be broadcast. Must be a non-nullable type.</typeparam>
public sealed class AsyncBroadcastMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task>
    where TMessage : notnull
{
    /// <summary>
    ///     Gets the event mediation settings that control broadcast behavior for this strategy instance.
    /// </summary>
    private readonly EventMediationSettings _settings;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AsyncBroadcastMediationStrategy{TMessage}" /> class.
    /// </summary>
    /// <param name="settings">
    ///     The event mediation settings that configure the broadcasting behavior, such as concurrency and
    ///     error handling.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="settings" /> is <see langword="null" />.</exception>
    public AsyncBroadcastMediationStrategy(EventMediationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>
    ///     Mediates the given message by broadcasting it to all relevant handlers according to the configured settings.
    ///     This method orchestrates the execution of pre-handlers, main handlers, and post-handlers, and delegates to error
    ///     handlers upon exception.
    /// </summary>
    /// <param name="message">The message to broadcast.</param>
    /// <param name="messageDependencies">A pre-filtered collection of handlers and their descriptors for the message pipeline.</param>
    /// <param name="executionContext">The execution context for the mediation.</param>
    /// <returns>A <see cref="Task" /> that represents the asynchronous completion of the entire broadcast operation.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if <paramref name="message" />, <paramref name="messageDependencies" />,
    ///     or <paramref name="executionContext" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if <see cref="EventMediationSettings.ThrowIfNoHandlerFound" /> is
    ///     <see langword="true" /> and no main handlers are found for the message.
    /// </exception>
    public async Task Mediate(TMessage message, IMessageDependencies messageDependencies, IExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        var executionTaskOfAllHandlers = Task.CompletedTask;
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = MediationTelemetry.StartMediation(message.GetType());
        var outcome = MediationOutcome.Succeeded;
        Exception? failure = null;
        string? reason = null;
        string? code = null;

        try
        {
            var decision = await messageDependencies
                .RunAsyncPreStages(message, executionContext.CancellationToken)
                .ConfigureAwait(false);

            if (decision.StopsPipeline)
            {
                outcome = decision.Outcome;
                reason = decision.Reason;
                code = decision.Code;

                // Only a Try call installs a capture, so this is a dictionary miss on every ordinary publish.
                MediationEndingCapture.Record(executionContext, decision);

                if (decision.IsRefusal)
                {
                    // An event produces no result, so a refusal has nothing a mapper could return and always reaches
                    // the publisher as an exception.
                    var refusal = decision.CreateRefusalException(message.GetType());
                    failure = refusal;
                    throw refusal;
                }

                return;
            }

            var allMainHandlers = messageDependencies.MainHandlers
                .Concat(messageDependencies.IndirectMainHandlers)
                .OrderBy(h => h.Descriptor.Priority)
                .ThenBy(h => h.Descriptor.RegistrationSequence)
                .ToList();

            if (allMainHandlers.Count == 0)
            {
                if (_settings.ThrowIfNoHandlerFound)
                {
                    throw new NoHandlerFoundException(typeof(TMessage));
                }

                return;
            }

            executionTaskOfAllHandlers = ExecuteHandlersByPriority(message, allMainHandlers, executionContext);
            await executionTaskOfAllHandlers.ConfigureAwait(false);

            await messageDependencies.RunAsyncPostHandlers(
                message,
                executionTaskOfAllHandlers,
                executionContext.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException canceledException)
        {
            outcome = MediationOutcome.Canceled;
            failure = canceledException;
            throw;
        }
        catch (Exception e) when (MediationExceptionFilters.IsRecoverableMediationException(e))
        {
            outcome = MediationOutcome.Failed;
            failure = e;

            await messageDependencies
                .RunAsyncErrorHandlers(message, executionTaskOfAllHandlers, e, executionContext)
                .ConfigureAwait(false);
        }
        finally
        {
            // An event produces no result. The task that tracks its handlers is not one, so completion handlers
            // see none rather than seeing a Task where a message result belongs.
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            MediationTelemetry.RecordMediation(activity, message.GetType(), outcome, code, elapsed);

            await messageDependencies
                .RunAsyncCompletionHandlers(
                    message,
                    executionContext,
                    outcome,
                    failure,
                    reason,
                    code,
                    messageResult: null,
                    elapsed)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Executes handlers by grouping them by priority and then processing each group
    ///     according to the <see cref="EventExecutionSettings.PriorityGroupsConcurrencyMode" /> setting.
    /// </summary>
    /// <param name="message">The message being broadcast to handlers.</param>
    /// <param name="handlers">The main handlers to execute, grouped by priority before invocation.</param>
    /// <param name="executionContext">The execution context propagated to each handler invocation.</param>
    /// <returns>A task that completes when all priority groups have finished executing.</returns>
    private async Task ExecuteHandlersByPriority(TMessage message,
                                                 IReadOnlyList<LazyHandler<IMessageHandler, IMainHandlerDescriptor>> handlers,
                                                 IExecutionContext executionContext)
    {
        var priorityGroups = handlers
            .GroupBy(h => h.Descriptor.Priority)
            .OrderBy(g => g.Key)
            .ToList();

        if (_settings.Execution.PriorityGroupsConcurrencyMode == ConcurrencyMode.Parallel)
        {
            var allGroupTasks = priorityGroups.Select(group => ExecuteHandlersInGroup(message, group.ToList(), executionContext)).ToArray();
            await AwaitParallelTasksAsync(allGroupTasks).ConfigureAwait(false);
        }
        else
        {
            foreach (var priorityGroup in priorityGroups)
            {
                await ExecuteHandlersInGroup(message, priorityGroup.ToList(), executionContext).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Executes a group of handlers that share the same priority level, respecting the
    ///     <see cref="EventExecutionSettings.HandlersWithinSamePriorityConcurrencyMode" /> setting.
    /// </summary>
    /// <param name="message">The message being broadcast to handlers.</param>
    /// <param name="handlersInGroup">The handlers that share the same priority value.</param>
    /// <param name="executionContext">The execution context propagated to each handler invocation.</param>
    /// <returns>A task that completes when every handler in the group has finished executing.</returns>
    private async Task ExecuteHandlersInGroup(TMessage message,
                                              IReadOnlyList<LazyHandler<IMessageHandler, IMainHandlerDescriptor>> handlersInGroup,
                                              IExecutionContext executionContext)
    {
        var orderedHandlers = handlersInGroup
            .OrderBy(h => h.Descriptor.RegistrationSequence)
            .ToList();

        if (_settings.Execution.HandlersWithinSamePriorityConcurrencyMode == ConcurrencyMode.Parallel)
        {
            var handlerTasks = orderedHandlers.Select(lazyHandler => ExecuteSingleHandler(message, lazyHandler, executionContext)).ToArray();
            await AwaitParallelTasksAsync(handlerTasks).ConfigureAwait(false);
        }
        else
        {
            foreach (var lazyHandler in orderedHandlers)
            {
                await ExecuteSingleHandler(message, lazyHandler, executionContext).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Awaits parallel handler tasks using the configured <see cref="EventExecutionSettings.ParallelFaultMode" />.
    /// </summary>
    /// <param name="tasks">The handler tasks executing concurrently.</param>
    /// <returns>A task that completes when every handler task has finished or faults according to fault mode.</returns>
    private async Task AwaitParallelTasksAsync(IReadOnlyList<Task> tasks)
    {
        if (_settings.Execution.ParallelFaultMode == ParallelFaultMode.AggregateAll)
        {
            var exceptions = new List<Exception>();

            foreach (var task in tasks)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception exception)
                {
                    // AggregateAll fault mode collects every handler failure before surfacing a combined exception.
                    exceptions.Add(exception);
                }
#pragma warning restore CA1031
            }

            if (exceptions.Count > 0)
            {
                throw exceptions.Count == 1
                    ? exceptions[0]
                    : new AggregateException(exceptions);
            }

            return;
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    ///     Resolves and executes a single message handler.
    /// </summary>
    /// <param name="message">The message passed to the handler.</param>
    /// <param name="lazyHandler">The lazily resolved handler and its descriptor.</param>
    /// <param name="executionContext">The execution context set on the ambient scope before the handler runs.</param>
    /// <returns>A task that completes when the handler has finished processing the message.</returns>
    /// <remarks>
    ///     <typeparamref name="TMessage" /> is the compile-time message type, which is not always the runtime type of the
    ///     message. The non-generic <c>IEventMediator.PublishAsync(IEvent, ...)</c> overload erases the event type to
    ///     <see cref="IEvent" />, and a base-typed variable erases it the same way through the generic overload. Handler
    ///     contracts are contravariant, so a handler registered for the concrete runtime type does not satisfy a type test
    ///     against the erased type. Such handlers are invoked through the non-generic entry point, which dispatches to the
    ///     closed contract the handler actually implements.
    /// </remarks>
    private static async Task ExecuteSingleHandler(TMessage message,
                                                   LazyHandler<IMessageHandler, IMainHandlerDescriptor> lazyHandler,
                                                   IExecutionContext executionContext)
    {
        using var _ = AmbientExecutionContext.CreateScope(executionContext);

        var handler = lazyHandler.Handler.Value;

        if (handler is IAsyncMessageHandler<TMessage> asyncHandler)
        {
            await asyncHandler.HandleAsync(message, executionContext.CancellationToken).ConfigureAwait(false);
            return;
        }

        if (handler is IMessageHandler<TMessage, Task> typedHandler)
        {
            await typedHandler.Handle(message).ConfigureAwait(false);
            return;
        }

        await ((Task) handler.Handle(message)).ConfigureAwait(false);
    }
}
