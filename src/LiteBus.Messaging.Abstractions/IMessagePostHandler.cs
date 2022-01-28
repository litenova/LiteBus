using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

public interface IMessagePostHandler
{
    Task PostHandleAsync(IHandleContext context);
}