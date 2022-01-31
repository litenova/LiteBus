using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Extensions;

public static class MessageContextExtensions
{
    public static async Task RunPreHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        foreach (var preHandler in messageContext.IndirectPreHandlers)
        {
            await preHandler.Instance.PreHandleAsync(handleContext);
        }

        foreach (var preHandler in messageContext.PreHandlers)
        {
            await preHandler.Instance.PreHandleAsync(handleContext);
        }
    }

    public static async Task RunErrorHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        foreach (var errorHandler in messageContext.IndirectErrorHandlers)
        {
            await errorHandler.Instance.HandleErrorAsync(handleContext);
        }

        foreach (var errorHandler in messageContext.ErrorHandlers)
        {
            await errorHandler.Instance.HandleErrorAsync(handleContext);
        }
    }

    public static async Task RunPostHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        foreach (var preHandler in messageContext.PostHandlers)
        {
            await preHandler.Instance.PostHandleAsync(handleContext);
        }

        foreach (var preHandler in messageContext.IndirectPostHandlers)
        {
            await preHandler.Instance.PostHandleAsync(handleContext);
        }
    }
}