namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The live refusal catalog store. Combines the write surface used during
///     module configuration with the read surface used at runtime.
///     Registered as a singleton in the DI container.
/// </summary>
/// <remarks>
///     Module builders depend on <see cref="IRefusalWriter" />.
///     Transports depend on <see cref="IRefusalReader" />.
///     This interface is the DI registration key for the shared singleton instance.
/// </remarks>
public interface IRefusalCatalog : IRefusalWriter, IRefusalReader
{
}
