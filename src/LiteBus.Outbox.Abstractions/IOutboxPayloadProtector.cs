using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Encrypts and decrypts outbox payloads at rest without sharing a DI registration with the inbox axis.
/// </summary>
/// <remarks>
///     Register through the outbox core builder. Outbox writers, transactional enqueue
///     paths, and outbox dispatchers resolve this type instead of <see cref="IPayloadEncryptor" /> so each axis can use a
///     different encryptor instance or key material. The module adapter forwards <see cref="IContextualPayloadEncryptor" />
///     overloads to the configured encryptor when it implements them; custom protectors can implement
///     <see cref="IContextualPayloadEncryptor" /> to receive the same durable metadata.
/// </remarks>
public interface IOutboxPayloadProtector : IPayloadEncryptor;
