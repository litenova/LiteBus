namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The live message contract store. Combines the write surface used during
///     module configuration with the read surface used at runtime.
///     Registered as a singleton in the DI container.
/// </summary>
/// <remarks>
///     Module builders depend on <see cref="IContractWriter" />.
///     Dispatchers and envelope factories depend on <see cref="IContractReader" />.
///     This interface is the DI registration key for the shared singleton instance.
/// </remarks>
public interface IMessageContractRegistry : IContractWriter, IContractReader
{
}