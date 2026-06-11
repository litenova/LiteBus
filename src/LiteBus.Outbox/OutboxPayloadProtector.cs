using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Adapts a configured <see cref="IPayloadEncryptor" /> to the outbox-specific protector registration key.
/// </summary>
internal sealed class OutboxPayloadProtector : IOutboxPayloadProtector
{
    /// <summary>
    ///     Gets the encryptor supplied by <see cref="OutboxModuleBuilder.UsePayloadEncryption" />.
    /// </summary>
    private readonly IPayloadEncryptor _encryptor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxPayloadProtector" /> class.
    /// </summary>
    /// <param name="encryptor">The encryptor used for outbox payload protection.</param>
    public OutboxPayloadProtector(IPayloadEncryptor encryptor)
    {
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
    }

    /// <inheritdoc />
    public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
    {
        return _encryptor.EncryptAsync(plaintext, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
    {
        return _encryptor.DecryptAsync(ciphertext, cancellationToken);
    }
}