using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Encrypts and decrypts outbox payloads at rest without sharing a DI registration with the inbox axis.
/// </summary>
/// <remarks>
///     Register through <see cref="OutboxModuleBuilder.UsePayloadEncryption" />. Outbox writers, transactional enqueue
///     paths, and outbox dispatchers resolve this type instead of <see cref="IPayloadEncryptor" /> so each axis can use a
///     different encryptor instance or key material.
/// </remarks>
public interface IOutboxPayloadProtector : IPayloadEncryptor;
