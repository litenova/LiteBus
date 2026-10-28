using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Adapts a configured <see cref="IPayloadEncryptor" /> to the outbox-specific protector registration key.
/// </summary>
/// <remarks>
///     Forwards the <see cref="IContextualPayloadEncryptor" /> overloads to the configured encryptor when it implements them; otherwise the plain contract is used.
/// </remarks>
internal sealed class OutboxPayloadProtector : IOutboxPayloadProtector, IContextualPayloadEncryptor
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
        ArgumentNullException.ThrowIfNull(encryptor);

        _encryptor = encryptor;
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

    /// <inheritdoc />
    public Task<string> EncryptAsync(string plaintext, PayloadProtectionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(context);

        return _encryptor is IContextualPayloadEncryptor contextualEncryptor
            ? contextualEncryptor.EncryptAsync(plaintext, context, cancellationToken)
            : _encryptor.EncryptAsync(plaintext, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> DecryptAsync(string ciphertext, PayloadProtectionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(context);

        return _encryptor is IContextualPayloadEncryptor contextualEncryptor
            ? contextualEncryptor.DecryptAsync(ciphertext, context, cancellationToken)
            : _encryptor.DecryptAsync(ciphertext, cancellationToken);
    }
}
