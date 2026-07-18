using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Encrypts and decrypts inbox payloads at rest without sharing a DI registration with the outbox axis.
/// </summary>
/// <remarks>
///     Register through the inbox core builder. Inbox writers, transactional accept
///     paths, and inbox dispatchers resolve this type instead of <see cref="IPayloadEncryptor" /> so each axis can use a
///     different encryptor instance or key material.
/// </remarks>
public interface IInboxPayloadProtector : IPayloadEncryptor;
