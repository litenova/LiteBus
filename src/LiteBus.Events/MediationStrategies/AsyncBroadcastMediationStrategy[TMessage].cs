#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.MediationStrategies;

/// <summary>
/// Represents an asynchronous broadcasting mediation strategy that processes a message across multiple handlers concurrently.
/// </summary>
/// <typeparam name="TMessage">The type of message being mediated.</typeparam>
/// <remarks>
/// This strategy performs the following sequence of operations:
/// 1. Executes pre-handlers.
/// 2. Broadcasts the message to all registered handlers concurrently.
/// 3. Executes post-handlers.
/// In case of any exception during the process, it delegates the error handling to the registered error handlers.
/// </remarks>
public sealed class AsyncBroadcastMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task> where TMessage : notnull
{
    private readonly EventMediationSettings _settings;

    public AsyncBroadcastMediationStrategy(EventMediationSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Mediates the given message by broadcasting it to all registered handlers concurrently.
    /// </summary>
    /// <param name="message">The message to be processed.</param>
    /// <param name="messageDependencies">The dependencies required for message handling, including registered handlers, pre-handlers, post-handlers, and error handlers.</param>
    /// <param name="executionContext"></param>
    /// <returns>A Task representing the asynchronous operation of the mediation process.</returns>
    public async Task Mediate(TMessage message, IMessageDependencies messageDependencies, IExecutionContext executionContext)
    {
        var executionTaskOfAllHandlers = Task.CompletedTask;

        var handlers = messageDependencies.Handlers
            .Where(x => _settings.Filters.HandlerPredicate(x.Descriptor.HandlerType))
            .ToList();

        if (handlers.Count == 0)
        {
            if (_settings.ThrowIfNoHandlerFound)
            {
                throw new InvalidOperationException($"No handler found for message type '{typeof(TMessage)}'.");
            }

            return;
        }

        try
        {
            await messageDependencies.RunAsyncPreHandlers(message);

            var sequentialExecutionTask = PublishSequentially(message, handlers);

            await sequentialExecutionTask;

            await messageDependencies.RunAsyncPostHandlers(message, sequentialExecutionTask);
        }
        catch (Exception e)
        {
            await messageDependencies.RunAsyncErrorHandlers(message, executionTaskOfAllHandlers, ExceptionDispatchInfo.Capture(e));
        }
    }

    private static async Task PublishSequentially(TMessage message, IEnumerable<LazyHandler<IMessageHandler, IMainHandlerDescriptor>> mainHandlers)
    {
        foreach (var lazyHandler in mainHandlers)
        {
            var handleTask = (Task) lazyHandler.Handler.Value.Handle(message);

            await handleTask;
        }
    }
}