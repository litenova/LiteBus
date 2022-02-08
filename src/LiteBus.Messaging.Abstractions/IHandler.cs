namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The non-generic base of all message handlers
/// </summary>
public interface IHandler
{
    object Handle(IHandleContext context);
}