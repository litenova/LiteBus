using System.Linq;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Workflows.Extensions;

public static class MessageContextExtensions
{
    public static async Task RunAsyncPreHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        var asyncIndirectPreHandlers = messageContext.IndirectPreHandlers
                                                     .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                  ExecutionMode.Asynchronous)
                                                     .Select(ph => ph.Instance);

        foreach (var asyncPreHandler in asyncIndirectPreHandlers)
        {
            await (Task) asyncPreHandler.Handle(handleContext);
        }

        var asyncPreHandlers = messageContext.PreHandlers
                                             .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Asynchronous)
                                             .Select(ph => ph.Instance);

        foreach (var asyncPreHandler in asyncPreHandlers)
        {
            await (Task) asyncPreHandler.Handle(handleContext);
        }
    }

    public static void RunSyncPreHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        var asyncIndirectPreHandlers = messageContext.IndirectPreHandlers
                                                     .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                  ExecutionMode.Synchronous)
                                                     .Select(ph => ph.Instance);

        foreach (var asyncPreHandler in asyncIndirectPreHandlers)
        {
            asyncPreHandler.Handle(handleContext);
        }

        var asyncPreHandlers = messageContext.PreHandlers
                                             .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Synchronous)
                                             .Select(ph => ph.Instance);

        foreach (var asyncPreHandler in asyncPreHandlers)
        {
            asyncPreHandler.Handle(handleContext);
        }
    }

    public static async Task RunAsyncErrorHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        var asyncErrorHandlers = messageContext.ErrorHandlers
                                               .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Asynchronous)
                                               .Select(ph => ph.Instance);

        foreach (var asyncErrorHandler in asyncErrorHandlers)
        {
            await (Task) asyncErrorHandler.Handle(handleContext);
        }

        var asyncIndirectErrorHandlers = messageContext.IndirectErrorHandlers
                                                       .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                    ExecutionMode.Asynchronous)
                                                       .Select(ph => ph.Instance);

        foreach (var asyncErrorHandler in asyncIndirectErrorHandlers)
        {
            await (Task) asyncErrorHandler.Handle(handleContext);
        }
    }

    public static void RunSyncErrorHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        var asyncErrorHandlers = messageContext.ErrorHandlers
                                               .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Synchronous)
                                               .Select(ph => ph.Instance);

        foreach (var asyncErrorHandler in asyncErrorHandlers)
        {
            asyncErrorHandler.Handle(handleContext);
        }

        var asyncIndirectErrorHandlers = messageContext.IndirectErrorHandlers
                                                       .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                    ExecutionMode.Synchronous)
                                                       .Select(ph => ph.Instance);

        foreach (var asyncErrorHandler in asyncIndirectErrorHandlers)
        {
            asyncErrorHandler.Handle(handleContext);
        }
    }

    public static async Task RunAsyncPostHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        var asyncPostHandlers = messageContext.PostHandlers
                                              .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Asynchronous)
                                              .Select(ph => ph.Instance);

        foreach (var asyncPostHandler in asyncPostHandlers)
        {
            await (Task) asyncPostHandler.Handle(handleContext);
        }

        var asyncIndirectPostHandlers = messageContext.IndirectPostHandlers
                                                      .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                   ExecutionMode.Asynchronous)
                                                      .Select(ph => ph.Instance);

        foreach (var asyncPostHandler in asyncIndirectPostHandlers)
        {
            await (Task) asyncPostHandler.Handle(handleContext);
        }
    }

    public static void RunSyncPostHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        var asyncPostHandlers = messageContext.PostHandlers
                                              .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Synchronous)
                                              .Select(ph => ph.Instance);

        foreach (var asyncPostHandler in asyncPostHandlers)
        {
            asyncPostHandler.Handle(handleContext);
        }

        var asyncIndirectPostHandlers = messageContext.IndirectPostHandlers
                                                      .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                   ExecutionMode.Synchronous)
                                                      .Select(ph => ph.Instance);

        foreach (var asyncPostHandler in asyncIndirectPostHandlers)
        {
            asyncPostHandler.Handle(handleContext);
        }
    }
}