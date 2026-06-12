using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

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

        if (messageDependencies.MainHandlers.Count == 0 && messageDependencies.IndirectMainHandlers.Count == 0)
        {
            await messageDependencies.RunAsyncPreHandlers(message, executionContext.CancellationToken).ConfigureAwait(false);

            if (_settings.ThrowIfNoHandlerFound)
            {
                throw new NoHandlerFoundException(typeof(TMessage));
            }

            return;
        }

        var executionTaskOfAllHandlers = Task.CompletedTask;

        try
        {
            await messageDependencies.RunAsyncPreHandlers(message, executionContext.CancellationToken).ConfigureAwait(false);

            var allMainHandlers = messageDependencies.MainHandlers
                .Concat(messageDependencies.IndirectMainHandlers)
                .OrderBy(h => h.Descriptor.Priority)
                .ThenBy(h => h.Descriptor.RegistrationSequence)
                .ToList();

            if (allMainHandlers.Count > 0)
            {
                executionTaskOfAllHandlers = ExecuteHandlersByPriority(message, allMainHandlers, executionContext);
                await executionTaskOfAllHandlers.ConfigureAwait(false);
            }

            await messageDependencies.RunAsyncPostHandlers(
                message,
                executionTaskOfAllHandlers,
                executionContext.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (MediationExceptionFilters.IsRecoverableMediationException(e))
        {
            await messageDependencies.RunAsyncErrorHandlers(
                message,
                executionTaskOfAllHandlers,
                ExceptionDispatchInfo.Capture(e),
                executionContext.CancellationToken).ConfigureAwait(false);
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
        }
    }
}
