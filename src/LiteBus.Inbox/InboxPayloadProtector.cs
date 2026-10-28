using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Adapts a configured <see cref="IPayloadEncryptor" /> to the inbox-specific protector registration key.
/// </summary>
/// <remarks>
///     Forwards the <see cref="IContextualPayloadEncryptor" /> overloads to the configured encryptor when it implements them; otherwise the plain contract is used.
/// </remarks>
internal sealed class InboxPayloadProtector : IInboxPayloadProtector, IContextualPayloadEncryptor
{
    /// <summary>
    ///     Gets the encryptor supplied by <see cref="InboxModuleBuilder.UsePayloadEncryption" />.
    /// </summary>
    private readonly IPayloadEncryptor _encryptor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxPayloadProtector" /> class.
    /// </summary>
    /// <param name="encryptor">The encryptor used for inbox payload protection.</param>
    public InboxPayloadProtector(IPayloadEncryptor encryptor)
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
