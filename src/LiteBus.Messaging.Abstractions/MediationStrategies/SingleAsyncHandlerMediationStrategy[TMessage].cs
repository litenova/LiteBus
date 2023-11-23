#nullable enable

using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

public sealed class SingleAsyncHandlerMediationStrategy<TMessage> : IMessageMediationStrategy<TMessage, Task> where TMessage : notnull
{
    public async Task Mediate(TMessage message, IMessageDependencies messageDependencies, IExecutionContext executionContext)
    {
        if (messageDependencies.Handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage), messageDependencies.Handlers.Count);
        }

        Task? messageResult = null;

        try
        {
            await messageDependencies.RunAsyncPreHandlers(message);

            var handler = messageDependencies.Handlers.Single().Handler.Value;

            messageResult = (Task) handler.Handle(message);

            await messageResult;

            await messageDependencies.RunAsyncPostHandlers(message, messageResult);
        }
        catch (Exception e)
        {
            await messageDependencies.RunAsyncErrorHandlers(message, messageResult, ExceptionDispatchInfo.Capture(e));
        }
    }
}