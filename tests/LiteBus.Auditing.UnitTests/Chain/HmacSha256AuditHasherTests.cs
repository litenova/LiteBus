using LiteBus.Auditing.Hashing;
using System.Text;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Verification;

namespace LiteBus.Auditing.UnitTests.Chain;

/// <summary>
///     Verifies that the HMAC-SHA-256 audit hasher keys chain hashes with a fixed or provided key.
/// </summary>
public sealed class HmacSha256AuditHasherTests
{
    /// <summary>
    ///     Builds the first test key.
    /// </summary>
    /// <returns>Four fixed key bytes.</returns>
    private static byte[] FirstKey() => [1, 2, 3, 4];

    /// <summary>
    ///     Builds the second test key.
    /// </summary>
    /// <returns>Four fixed key bytes that differ from the first key.</returns>
    private static byte[] SecondKey() => [5, 6, 7, 8];

    [Fact]
    public async Task The_same_key_and_bytes_hash_to_the_same_value()
    {
        var hasher = new HmacSha256AuditHasher(FirstKey());
        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1"));

        var first = await hasher.HashAsync(data, CancellationToken.None).ConfigureAwait(false);
        var second = await hasher.HashAsync(data, CancellationToken.None).ConfigureAwait(false);

        second.Should().Be(first);
    }

    [Fact]
    public async Task Different_keys_hash_differently()
    {
        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1"));

        var first = await new HmacSha256AuditHasher(FirstKey())
            .HashAsync(data, CancellationToken.None)
            .ConfigureAwait(false);
        var second = await new HmacSha256AuditHasher(SecondKey())
            .HashAsync(data, CancellationToken.None)
            .ConfigureAwait(false);

        second.Should().NotBe(first);
    }

    [Fact]
    public async Task The_keyed_hash_differs_from_the_plain_digest()
    {
        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1"));

        var keyed = await new HmacSha256AuditHasher(FirstKey())
            .HashAsync(data, CancellationToken.None)
            .ConfigureAwait(false);
        var plain = await new Sha256AuditHasher()
            .HashAsync(data, CancellationToken.None)
            .ConfigureAwait(false);

        keyed.Should().NotBe(plain);
    }

    [Fact]
    public async Task The_key_provider_supplies_the_key()
    {
        var calls = 0;
        var hasher = new HmacSha256AuditHasher(cancellationToken =>
        {
            calls++;
            return ValueTask.FromResult(FirstKey());
        });
        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1"));

        var provided = await hasher.HashAsync(data, CancellationToken.None).ConfigureAwait(false);
        var fixedKey = await new HmacSha256AuditHasher(FirstKey())
            .HashAsync(data, CancellationToken.None)
            .ConfigureAwait(false);

        provided.Should().Be(fixedKey);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task The_provider_is_asked_per_operation_so_a_rotation_takes_effect()
    {
        var keys = new Queue<byte[]>([FirstKey(), SecondKey()]);
        var hasher = new HmacSha256AuditHasher(_ => ValueTask.FromResult(keys.Dequeue()));
        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1"));

        var first = await hasher.HashAsync(data, CancellationToken.None).ConfigureAwait(false);
        var second = await hasher.HashAsync(data, CancellationToken.None).ConfigureAwait(false);

        second.Should().NotBe(first);
    }

    [Fact]
    public void An_empty_key_is_rejected()
    {
        var act = () => new HmacSha256AuditHasher(ReadOnlyMemory<byte>.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_null_provider_is_rejected()
    {
        var act = () => new HmacSha256AuditHasher((Func<CancellationToken, ValueTask<byte[]>>) null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_provider_returning_no_key_fails_the_hash()
    {
        var hasher = new HmacSha256AuditHasher(_ => ValueTask.FromResult<byte[]>([]));

        var act = () => hasher
            .HashAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1")), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task A_cancelled_token_throws()
    {
        var hasher = new HmacSha256AuditHasher(FirstKey());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(false);

        var act = () => hasher
            .HashAsync(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("orders.step-1")), cancelled.Token)
            .AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }
}
