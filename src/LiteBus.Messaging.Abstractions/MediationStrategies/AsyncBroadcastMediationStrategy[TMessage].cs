#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

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
    /// <summary>
    /// Mediates the given message by broadcasting it to all registered handlers concurrently.
    /// </summary>
    /// <param name="message">The message to be processed.</param>
    /// <param name="messageDependencies">The dependencies required for message handling, including registered handlers, pre-handlers, post-handlers, and error handlers.</param>
    /// <returns>A Task representing the asynchronous operation of the mediation process.</returns>
    public async Task Mediate(TMessage message, IMessageDependencies messageDependencies)
    {
        var executionTaskOfAllHandlers = Task.CompletedTask;

        try
        {
            await messageDependencies.RunAsyncPreHandlers(message);

            var tasks = messageDependencies.Handlers.Select(h => (Task) h.Value.Handle(message));

            executionTaskOfAllHandlers = Task.WhenAll(tasks);
            await executionTaskOfAllHandlers;

            await messageDependencies.RunPostHandlers(message, executionTaskOfAllHandlers);
        }
        catch (Exception e)
        {
            if (messageDependencies.ErrorHandlers.Count + messageDependencies.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            await messageDependencies.RunErrorHandlers(message, executionTaskOfAllHandlers, e);
        }
    }
}