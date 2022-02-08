namespace LiteBus.Messaging.Abstractions;

public interface IErrorHandler
{
    object Handle(IHandleContext context);
}