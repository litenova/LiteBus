using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Workflows.Execution.Exceptions;
using LiteBus.Messaging.Workflows.Extensions;
using LiteBus.Messaging.Workflows.Utilities;

namespace LiteBus.Messaging.Workflows.Execution;

public class SingleStreamHandlerExecutionWorkflow<TMessage, TMessageResult> :
    IExecutionWorkflow<TMessage, IAsyncEnumerable<TMessageResult>> where TMessage : notnull
{
    private readonly CancellationToken _cancellationToken;

    public SingleStreamHandlerExecutionWorkflow(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public async IAsyncEnumerable<TMessageResult> Execute(TMessage message,
                                                          IMessageContext messageContext)
    {
        var handlers = messageContext.Handlers
                                     .Where(h => h.Descriptor.ExecutionMode == ExecutionMode.AsynchronousStreaming)
                                     .ToList();

        if (handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage));
        }

        var handleContext = new HandleContext(message);
        handleContext.Data.Set(_cancellationToken);
        var result = AsyncEnumerable.Empty<TMessageResult>();

        try
        {
            await messageContext.RunAsyncPreHandlers(handleContext);

            var handler = handlers.Single().Instance;

            result = (IAsyncEnumerable<TMessageResult>) handler!.Handle(handleContext);
        }
        catch (Exception e)
        {
            if (messageContext.ErrorHandlers.Count + messageContext.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            await messageContext.RunAsyncErrorHandlers(handleContext);
        }

        await foreach (var messageResult in result.WithCancellation(_cancellationToken))
        {
            yield return messageResult;
        }

        handleContext.MessageResult = result;

        try
        {
            await messageContext.RunAsyncPostHandlers(handleContext);
        }
        catch (Exception e)
        {
            if (messageContext.ErrorHandlers.Count + messageContext.IndirectErrorHandlers.Count == 0)
            {
                throw;
            }

            handleContext.Exception = e;

            await messageContext.RunAsyncErrorHandlers(handleContext);
        }
    }
}