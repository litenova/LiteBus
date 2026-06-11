namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Creates dependency injection scopes for background processor message dispatch.
/// </summary>
/// <remarks>
///     Processors invoke this factory once per leased envelope so scoped handlers such as
///     <c>DbContext</c>-backed command handlers resolve from a fresh scope instead of the root provider.
/// </remarks>
public interface IMessageDispatchScopeFactory
{
    /// <summary>
    ///     Creates a new dispatch scope.
    /// </summary>
    /// <returns>A scope whose provider should be used for one message dispatch.</returns>
    IMessageDispatchScope CreateScope();
}