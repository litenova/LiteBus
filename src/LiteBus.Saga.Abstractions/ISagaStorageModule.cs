using LiteBus.Runtime.Abstractions;

namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Marks a storage module that provides exactly one <see cref="ISagaStore" /> implementation.
/// </summary>
public interface ISagaStorageModule : IModule;
