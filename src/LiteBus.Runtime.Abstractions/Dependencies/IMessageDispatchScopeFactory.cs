namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Creates host-container scopes for message dispatch.
/// </summary>
/// <remarks>
///     A container adapter registers one implementation. Root-provider dispatch requires
///     an explicit <c>RootMessageDispatchScopeFactory</c> registration by a custom host.
/// </remarks>
public interface IMessageDispatchScopeFactory
{
    /// <summary>
    ///     Creates a new dispatch scope.
    /// </summary>
    /// <returns>A scope whose provider is used for one dispatch.</returns>
    IMessageDispatchScope CreateScope();
}
