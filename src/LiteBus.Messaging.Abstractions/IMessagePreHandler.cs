using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

public interface IMessagePreHandler
{
    Task PreHandleAsync(IHandleContext context);
}
