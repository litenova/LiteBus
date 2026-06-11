namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The live message descriptor store. Combines the write surface used during
///     module configuration with the read surface used by the mediator at runtime.
///     Registered as a singleton in the DI container.
/// </summary>
/// <remarks>
///     Module builders depend on <see cref="IMessageWriter" /> or this interface.
///     The mediator and resolve strategies depend on <see cref="IMessageReader" />.
///     This interface is the DI registration key for the shared singleton instance.
/// </remarks>
public interface IMessageRegistry : IMessageWriter, IMessageReader
{
}