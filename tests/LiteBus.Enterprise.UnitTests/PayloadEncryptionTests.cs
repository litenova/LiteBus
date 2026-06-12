using AwesomeAssertions;
using LiteBus.Commands.Abstractions;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Enterprise.UnitTests;

/// <summary>
///     Verifies payload encryption on accept and decrypt on dispatch.
/// </summary>
public sealed class PayloadEncryptionTests
{
    /// <summary>
    ///     Verifies stored payloads are encrypted and dispatch decrypts them.
    /// </summary>
    [Fact]
    public async Task Inbox_RoundTripsEncryptedPayloadThroughDispatch()
    {
        var store = new InMemoryInboxStore();
        IInboxPayloadProtector encryptor = new PrefixPayloadEncryptor("enc:");
        var registry = new MessageContractRegistry();
        registry.Register<TestCommand>("test-command");
        var serializer = new SystemTextJsonMessageSerializer();

        var inbox = new Inbox.Inbox(
            store,
            new InboxEnvelopeFactory(registry, serializer, TimeProvider.System, encryptor),
            TimeProvider.System);

        await inbox.AcceptAsync(InboxAcceptItem<TestCommand>.From(new TestCommand { Value = "secret" }));

        var stored = store.GetAll().Single();
        stored.Payload.Should().StartWith("enc:");

        var dispatcher = new CapturingInboxDispatcher(serializer, encryptor);
        await dispatcher.DispatchAsync(stored);

        CapturingInboxDispatcher.LastValue.Should().Be("secret");
    }

    /// <summary>
    ///     Prefix-based test encryptor.
    /// </summary>
    private sealed class PrefixPayloadEncryptor : IInboxPayloadProtector
    {
        /// <summary>
        ///     The ciphertext prefix.
        /// </summary>
        private readonly string _prefix;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PrefixPayloadEncryptor" /> class.
        /// </summary>
        /// <param name="prefix">The ciphertext prefix.</param>
        public PrefixPayloadEncryptor(string prefix)
        {
            _prefix = prefix;
        }

        /// <inheritdoc />
        public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_prefix + plaintext);
        }

        /// <inheritdoc />
        public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ciphertext[_prefix.Length..]);
        }
    }

    /// <summary>
    ///     Test command payload.
    /// </summary>
    private sealed class TestCommand : ICommand
    {
        /// <summary>
        ///     Gets or sets the value.
        /// </summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    ///     Dispatcher that decrypts and deserializes the stored payload for verification.
    /// </summary>
    private sealed class CapturingInboxDispatcher : IInboxDispatcher
    {
        /// <summary>
        ///     Gets the encryptor used to decrypt stored payloads.
        /// </summary>
        private readonly IInboxPayloadProtector _encryptor;

        /// <summary>
        ///     Gets the serializer used to hydrate payloads.
        /// </summary>
        private readonly IMessageSerializer _serializer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CapturingInboxDispatcher" /> class.
        /// </summary>
        /// <param name="serializer">The serializer used to hydrate payloads.</param>
        /// <param name="encryptor">The encryptor used to decrypt stored payloads.</param>
        public CapturingInboxDispatcher(IMessageSerializer serializer, IInboxPayloadProtector encryptor)
        {
            _serializer = serializer;
            _encryptor = encryptor;
        }

        /// <summary>
        ///     Gets the last deserialized command value.
        /// </summary>
        public static string LastValue { get; private set; } = string.Empty;

        /// <inheritdoc />
        public async Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            var payload = await PayloadProtection.UnprotectAsync(envelope.Payload, _encryptor, cancellationToken)
                ;

            var command = await _serializer.DeserializeAsync(typeof(TestCommand), payload, cancellationToken)
                ;

            LastValue = ((TestCommand) command).Value;
        }
    }
}