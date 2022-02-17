using System.Linq;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Workflows.Extensions;

public static class MessageContextExtensions
{
    public static async Task RunAsyncPreHandlers(this IResolutionContext resolutionContext, HandleContext handleContext)
    {
        var asyncIndirectPreHandlers = resolutionContext.IndirectPreHandlers
                                                     .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                  ExecutionMode.Asynchronous)
                                                     .Select(ph => ph.Instance);

        foreach (var asyncPreHandler in asyncIndirectPreHandlers)
        {
            await (Task) asyncPreHandler.Handle(handleContext);
        }

        var asyncPreHandlers = resolutionContext.PreHandlers
                                             .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Asynchronous)
                                             .Select(ph => ph.Instance);

        foreach (var asyncPreHandler in asyncPreHandlers)
        {
            await (Task) asyncPreHandler.Handle(handleContext);
        }
    }

    public static void RunSyncPreHandlers(this IResolutionContext resolutionContext, HandleContext handleContext)
    {
        var asyncIndirectPreHandlers = resolutionContext.IndirectPreHandlers
                                                     .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                  ExecutionMode.Synchronous)
                                                     .Select(ph => ph.Instance);

        foreach (var asyncPreHandler in asyncIndirectPreHandlers)
        {
            asyncPreHandler.Handle(handleContext);
        }

        var asyncPreHandlers = resolutionContext.PreHandlers
                                             .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Synchronous)
                                             .Select(ph => ph.Instance);

        foreach (var asyncPreHandler in asyncPreHandlers)
        {
            asyncPreHandler.Handle(handleContext);
        }
    }

    public static async Task RunAsyncErrorHandlers(this IResolutionContext resolutionContext, HandleContext handleContext)
    {
        var asyncErrorHandlers = resolutionContext.ErrorHandlers
                                               .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Asynchronous)
                                               .Select(ph => ph.Instance);

        foreach (var asyncErrorHandler in asyncErrorHandlers)
        {
            await (Task) asyncErrorHandler.Handle(handleContext);
        }

        var asyncIndirectErrorHandlers = resolutionContext.IndirectErrorHandlers
                                                       .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                    ExecutionMode.Asynchronous)
                                                       .Select(ph => ph.Instance);

        foreach (var asyncErrorHandler in asyncIndirectErrorHandlers)
        {
            await (Task) asyncErrorHandler.Handle(handleContext);
        }
    }

    public static void RunSyncErrorHandlers(this IResolutionContext resolutionContext, HandleContext handleContext)
    {
        var asyncErrorHandlers = resolutionContext.ErrorHandlers
                                               .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Synchronous)
                                               .Select(ph => ph.Instance);

        foreach (var asyncErrorHandler in asyncErrorHandlers)
        {
            asyncErrorHandler.Handle(handleContext);
        }

        var asyncIndirectErrorHandlers = resolutionContext.IndirectErrorHandlers
                                                       .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                    ExecutionMode.Synchronous)
                                                       .Select(ph => ph.Instance);

        foreach (var asyncErrorHandler in asyncIndirectErrorHandlers)
        {
            asyncErrorHandler.Handle(handleContext);
        }
    }

    public static async Task RunAsyncPostHandlers(this IResolutionContext resolutionContext, HandleContext handleContext)
    {
        var asyncPostHandlers = resolutionContext.PostHandlers
                                              .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Asynchronous)
                                              .Select(ph => ph.Instance);

        foreach (var asyncPostHandler in asyncPostHandlers)
        {
            await (Task) asyncPostHandler.Handle(handleContext);
        }

        var asyncIndirectPostHandlers = resolutionContext.IndirectPostHandlers
                                                      .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                   ExecutionMode.Asynchronous)
                                                      .Select(ph => ph.Instance);

        foreach (var asyncPostHandler in asyncIndirectPostHandlers)
        {
            await (Task) asyncPostHandler.Handle(handleContext);
        }
    }

    public static void RunSyncPostHandlers(this IResolutionContext resolutionContext, HandleContext handleContext)
    {
        var asyncPostHandlers = resolutionContext.PostHandlers
                                              .Where(ph => ph.Descriptor.ExecutionMode == ExecutionMode.Synchronous)
                                              .Select(ph => ph.Instance);

        foreach (var asyncPostHandler in asyncPostHandlers)
        {
            asyncPostHandler.Handle(handleContext);
        }

        var asyncIndirectPostHandlers = resolutionContext.IndirectPostHandlers
                                                      .Where(ph => ph.Descriptor.ExecutionMode ==
                                                                   ExecutionMode.Synchronous)
                                                      .Select(ph => ph.Instance);

        foreach (var asyncPostHandler in asyncIndirectPostHandlers)
        {
            asyncPostHandler.Handle(handleContext);
        }
    }
}