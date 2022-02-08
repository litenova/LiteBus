namespace LiteBus.Messaging.Abstractions;

public interface IPreHandler
{
    object Handle(IHandleContext context);
}