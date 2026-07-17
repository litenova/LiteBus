using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Runtime.UnitTests.Serialization;

/// <summary>
///     Verifies optional payload protection pass-through and encryptor delegation.
/// </summary>
public sealed class PayloadProtectionTests
{
    /// <summary>
    ///     Verifies payload text is returned unchanged when no encryptor is configured.
    /// </summary>
    [Fact]
    public async Task Operations_WithoutEncryptor_ReturnOriginalPayload()
    {
        const string payload = "{\"orderId\":\"order-42\"}";

        var protectedPayload = await PayloadProtection.ProtectAsync(payload, null).ConfigureAwait(false);
        var unprotectedPayload = await PayloadProtection.UnprotectAsync(payload, null).ConfigureAwait(false);

        protectedPayload.Should().BeSameAs(payload);
        unprotectedPayload.Should().BeSameAs(payload);
    }

    /// <summary>
    ///     Verifies protection delegates to the encryptor and forwards cancellation.
    /// </summary>
    [Fact]
    public async Task ProtectAsync_WithEncryptor_DelegatesEncryption()
    {
        var encryptor = new RecordingEncryptor();
        using var source = new CancellationTokenSource();

        var result = await PayloadProtection.ProtectAsync("plain", encryptor, source.Token).ConfigureAwait(false);

        result.Should().Be("encrypted:plain");
        encryptor.EncryptedPayload.Should().Be("plain");
        encryptor.EncryptionToken.Should().Be(source.Token);
        encryptor.DecryptedPayload.Should().BeNull();
    }

    /// <summary>
    ///     Verifies unprotection delegates to the encryptor and forwards cancellation.
    /// </summary>
    [Fact]
    public async Task UnprotectAsync_WithEncryptor_DelegatesDecryption()
    {
        var encryptor = new RecordingEncryptor();
        using var source = new CancellationTokenSource();

        var result = await PayloadProtection.UnprotectAsync("cipher", encryptor, source.Token).ConfigureAwait(false);

        result.Should().Be("decrypted:cipher");
        encryptor.DecryptedPayload.Should().Be("cipher");
        encryptor.DecryptionToken.Should().Be(source.Token);
        encryptor.EncryptedPayload.Should().BeNull();
    }

    /// <summary>
    ///     Verifies null payloads are rejected before optional encryptor delegation.
    /// </summary>
    [Fact]
    public async Task Operations_WhenPayloadIsNull_ThrowArgumentNullException()
    {
        var encryptor = new RecordingEncryptor();

        var protect = () => PayloadProtection.ProtectAsync(null!, encryptor);
        var unprotect = () => PayloadProtection.UnprotectAsync(null!, encryptor);

        await protect.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        await unprotect.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        encryptor.EncryptedPayload.Should().BeNull();
        encryptor.DecryptedPayload.Should().BeNull();
    }

    /// <summary>
    ///     Verifies encryptor failures propagate without losing the provider exception type.
    /// </summary>
    [Fact]
    public async Task Operations_WhenEncryptorFails_PropagateProviderException()
    {
        var encryptor = new RecordingEncryptor
        {
            Exception = new InvalidOperationException("key unavailable")
        };

        var protect = () => PayloadProtection.ProtectAsync("plain", encryptor);
        var unprotect = () => PayloadProtection.UnprotectAsync("cipher", encryptor);

        await protect.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("key unavailable").ConfigureAwait(false);
        await unprotect.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("key unavailable").ConfigureAwait(false);
    }

    private sealed class RecordingEncryptor : IPayloadEncryptor
    {
        internal string? DecryptedPayload { get; private set; }

        internal CancellationToken DecryptionToken { get; private set; }

        internal string? EncryptedPayload { get; private set; }

        internal CancellationToken EncryptionToken { get; private set; }

        internal Exception? Exception { get; init; }

        public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
        {
            EncryptedPayload = plaintext;
            EncryptionToken = cancellationToken;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult($"encrypted:{plaintext}");
        }

        public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
        {
            DecryptedPayload = ciphertext;
            DecryptionToken = cancellationToken;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult($"decrypted:{ciphertext}");
        }
    }
}
