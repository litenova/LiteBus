using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Adapts a configured <see cref="IPayloadEncryptor" /> to the inbox-specific protector registration key.
/// </summary>
internal sealed class InboxPayloadProtector : IInboxPayloadProtector
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