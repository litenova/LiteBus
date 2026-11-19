using System.Text;
using LiteBus.Messaging.Audit;

namespace LiteBus.Mediator.UnitTests.Auditing.Chain;

/// <summary>
///     Verifies that the plain SHA-256 audit hasher digests chain bytes deterministically.
/// </summary>
public sealed class Sha256AuditHasherTests
{
    [Fact]
    public async Task The_same_bytes_hash_to_the_same_value()
    {
        var hasher = new Sha256AuditHasher();
        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1"));

        var first = await hasher.HashAsync(data, CancellationToken.None).ConfigureAwait(false);
        var second = await hasher.HashAsync(data, CancellationToken.None).ConfigureAwait(false);

        second.Should().Be(first);
    }

    [Fact]
    public async Task Different_bytes_hash_differently()
    {
        var hasher = new Sha256AuditHasher();

        var first = await hasher
            .HashAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1")), CancellationToken.None)
            .ConfigureAwait(false);
        var second = await hasher
            .HashAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-2")), CancellationToken.None)
            .ConfigureAwait(false);

        second.Should().NotBe(first);
    }

    [Fact]
    public async Task The_hash_matches_the_published_SHA256_vector()
    {
        var hasher = new Sha256AuditHasher();

        var hash = await hasher
            .HashAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("abc")), CancellationToken.None)
            .ConfigureAwait(false);

        hash.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public async Task The_hash_is_lowercase_hex_of_64_characters()
    {
        var hasher = new Sha256AuditHasher();

        var hash = await hasher
            .HashAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1")), CancellationToken.None)
            .ConfigureAwait(false);

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task A_cancelled_token_throws()
    {
        var hasher = new Sha256AuditHasher();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(false);

        var act = () => hasher
            .HashAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1")), cancelled.Token)
            .AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }
}
