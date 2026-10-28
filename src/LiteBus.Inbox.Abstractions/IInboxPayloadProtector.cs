using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Encrypts and decrypts inbox payloads at rest without sharing a DI registration with the outbox axis.
/// </summary>
/// <remarks>
///     Register through the inbox core builder. Inbox writers, transactional accept
///     paths, and inbox dispatchers resolve this type instead of <see cref="IPayloadEncryptor" /> so each axis can use a
///     different encryptor instance or key material. The module adapter forwards <see cref="IContextualPayloadEncryptor" />
///     overloads to the configured encryptor when it implements them; custom protectors can implement
///     <see cref="IContextualPayloadEncryptor" /> to receive the same durable metadata.
/// </remarks>
public interface IInboxPayloadProtector : IPayloadEncryptor;
