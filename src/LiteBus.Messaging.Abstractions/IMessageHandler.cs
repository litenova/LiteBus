namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The non-generic base of all message handlers
/// </summary>
public interface IMessageHandler
{
    object Handle(IHandleContext context);
}