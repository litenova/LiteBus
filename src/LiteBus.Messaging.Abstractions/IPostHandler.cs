namespace LiteBus.Messaging.Abstractions;

public interface IPostHandler
{
    object Handle(IHandleContext context);
}