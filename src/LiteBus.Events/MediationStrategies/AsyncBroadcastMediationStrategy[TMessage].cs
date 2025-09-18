using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.MediationStrategies;

/// <summary>
/// Implements a publish-subscribe message mediation strategy that broadcasts a message to multiple handlers.
/// This strategy orchestrates the full execution pipeline, including pre-handlers, main handlers, post-handlers,
/// and error handlers, while respecting configured concurrency settings.
/// </summary>
/// <typeparam name="TMessage">The type of the message to be broadcast. Must be a non-nullable type.</typeparam>
public sealed class AsyncBroadcastMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task>
    where TMessage : notnull
{
    private readonly EventMediationSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncBroadcastMediationStrategy{TMessage}"/> class.
    /// </summary>
    /// <param name="settings">The event mediation settings that configure the broadcasting behavior, such as concurrency and error handling.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="settings"/> is <c>null</c>.</exception>
    public AsyncBroadcastMediationStrategy(EventMediationSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Mediates the given message by broadcasting it to all relevant handlers according to the configured settings.
    /// This method orchestrates the execution of pre-handlers, main handlers, and post-handlers, and delegates to error handlers upon exception.
    /// </summary>
    /// <param name="message">The message to broadcast.</param>
    /// <param name="messageDependencies">A pre-filtered collection of handlers and their descriptors for the message pipeline.</param>
    /// <param name="executionContext">The execution context for the mediation.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous completion of the entire broadcast operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="message"/>, <paramref name="messageDependencies"/>, or <paramref name="executionContext"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="EventMediationSettings.ThrowIfNoHandlerFound"/> is <c>true</c> and no main handlers are found for the message.</exception>
    public async Task Mediate(TMessage message, IMessageDependencies messageDependencies, IExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        // Check if any main handlers are left after filtering in the messaging layer.
        // This check is specific to events, as commands/queries expect exactly one handler.
        if (messageDependencies.MainHandlers.Count == 0 && messageDependencies.IndirectMainHandlers.Count == 0)
        {
            if (_settings.ThrowIfNoHandlerFound)
            {
                throw new NoHandlerFoundException(typeof(TMessage));
            }

            // If no handlers are found and we're not configured to throw, we can exit early.
            // Pre/Post handlers should not run if there's no main handler to act upon.
            return;
        }

        Task executionTaskOfAllHandlers = Task.CompletedTask;

        try
        {
            // 1. Run all pre-handlers (direct and indirect)
            await messageDependencies.RunAsyncPreHandlers(message);

            // 2. Combine and execute all main handlers according to the specified concurrency model
            var allMainHandlers = messageDependencies.MainHandlers.Concat(messageDependencies.IndirectMainHandlers).ToList();

            if (allMainHandlers.Count > 0)
            {
                executionTaskOfAllHandlers = ExecuteHandlersByPriority(message, allMainHandlers);
                await executionTaskOfAllHandlers;
            }

            // 3. Run all post-handlers (direct and indirect)
            await messageDependencies.RunAsyncPostHandlers(message, executionTaskOfAllHandlers);
        }
        catch (Exception e)
        {
            // 4. On any exception, run all error handlers.
            // The RunAsyncErrorHandlers extension will re-throw the exception if no error handlers are registered.
            await messageDependencies.RunAsyncErrorHandlers(message, executionTaskOfAllHandlers, ExceptionDispatchInfo.Capture(e));
        }
    }

    /// <summary>
    /// Executes handlers by grouping them by priority and then processing each group
    /// according to the <see cref="EventMediationExecutionSettings.PriorityGroupsConcurrencyMode"/> setting.
    /// </summary>
    private async Task ExecuteHandlersByPriority(TMessage message,
                                                 IReadOnlyList<LazyHandler<IMessageHandler, IMainHandlerDescriptor>> handlers)
    {
        var priorityGroups = handlers
            .GroupBy(h => h.Descriptor.Priority)
            .OrderBy(g => g.Key)
            .ToList();

        if (_settings.Execution.PriorityGroupsConcurrencyMode == ConcurrencyMode.Parallel)
        {
            var allGroupTasks = priorityGroups.Select(group => ExecuteHandlersInGroup(message, group.ToList()));
            await Task.WhenAll(allGroupTasks);
        }
        else
        {
            foreach (var priorityGroup in priorityGroups)
            {
                await ExecuteHandlersInGroup(message, priorityGroup.ToList());
            }
        }
    }

    /// <summary>
    /// Executes a group of handlers that share the same priority level, respecting the
    /// <see cref="EventMediationExecutionSettings.HandlersWithinSamePriorityConcurrencyMode"/> setting.
    /// </summary>
    private async Task ExecuteHandlersInGroup(TMessage message,
                                              IReadOnlyList<LazyHandler<IMessageHandler, IMainHandlerDescriptor>> handlersInGroup)
    {
        if (_settings.Execution.HandlersWithinSamePriorityConcurrencyMode == ConcurrencyMode.Parallel)
        {
            var handlerTasks = handlersInGroup.Select(lazyHandler => ExecuteSingleHandler(message, lazyHandler));
            await Task.WhenAll(handlerTasks);
        }
        else
        {
            foreach (var lazyHandler in handlersInGroup)
            {
                await ExecuteSingleHandler(message, lazyHandler);
            }
        }
    }

    /// <summary>
    /// Resolves and executes a single message handler.
    /// </summary>
    private static async Task ExecuteSingleHandler(TMessage message,
                                                   LazyHandler<IMessageHandler, IMainHandlerDescriptor> lazyHandler)
    {
        var handleTask = (Task) lazyHandler.Handler.Value.Handle(message);
        await handleTask;
    }
}