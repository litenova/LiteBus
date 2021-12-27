using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Extensions;

public static class MessageContextExtensions
{
    public static async Task RunPreHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        foreach (var preHandler in messageContext.IndirectPreHandlers)
        {
            await preHandler.Value.PreHandleAsync(handleContext);
        }

        foreach (var preHandler in messageContext.PreHandlers)
        {
            await preHandler.Value.PreHandleAsync(handleContext);
        }
    }
    
    public static async Task RunErrorHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        foreach (var errorHandler in messageContext.IndirectErrorHandlers)
        {
            await errorHandler.Value.HandleErrorAsync(handleContext);
        }

        foreach (var errorHandler in messageContext.ErrorHandlers)
        {
            await errorHandler.Value.HandleErrorAsync(handleContext);
        }
    }

    public static async Task RunPostHandlers(this IMessageContext messageContext, HandleContext handleContext)
    {
        foreach (var preHandler in messageContext.PostHandlers)
        {
            await preHandler.Value.PostHandleAsync(handleContext);
        }

        foreach (var preHandler in messageContext.IndirectPostHandlers)
        {
            await preHandler.Value.PostHandleAsync(handleContext);
        }
    }
}