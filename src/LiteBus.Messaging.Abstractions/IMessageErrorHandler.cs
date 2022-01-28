using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

public interface IMessageErrorHandler
{
    Task HandleErrorAsync(IHandleContext context);
}