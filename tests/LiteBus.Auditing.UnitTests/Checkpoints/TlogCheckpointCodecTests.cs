using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.UnitTests.Checkpoints;

/// <summary>
///     Verifies that the checkpoint codec renders C2SP checkpoint text and reads it back.
/// </summary>
public sealed class TlogCheckpointCodecTests
{
    /// <summary>
    ///     Builds a signed checkpoint with a label.
    /// </summary>
    /// <returns>The checkpoint.</returns>
    private static AuditCheckpoint Checkpoint()
    {
        return new AuditCheckpoint(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "orders/2026",
            128,
            128,
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            "sha256-v2",
            AuditCheckpointKind.Regular,
            new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero))
        {
            Label = "daily",
            PublishState = CheckpointPublishState.Published,
            Signatures =
            [
                new AuditCheckpointSignature("example.com/log", "QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo="),
            ],
        };
    }

    [Fact]
    public void Encode_writes_the_origin_size_and_root_hash_lines()
    {
        var text = TlogCheckpointCodec.Encode(Checkpoint());
        var lines = text.Split('\n');

        lines[0].Should().Be("orders/2026");
        lines[1].Should().Be("128");
        lines[2].Should().Be(Convert.ToBase64String(Convert.FromHexString("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")));
    }

    [Fact]
    public void Encode_writes_one_signature_line_per_signature()
    {
        var text = TlogCheckpointCodec.Encode(Checkpoint());

        text.Should().Contain("— example.com/log QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo=\n");
    }

    [Fact]
    public void Encode_writes_no_signature_line_for_an_unsigned_checkpoint()
    {
        var checkpoint = Checkpoint() with { Signatures = [], Signature = null };

        var text = TlogCheckpointCodec.Encode(checkpoint);

        text.Should().EndWith("\n\n");
        text.Should().NotContain("—");
    }

    [Fact]
    public void Round_trip_preserves_identity_coverage_label_and_signatures()
    {
        var checkpoint = Checkpoint();

        var parsed = TlogCheckpointCodec.Parse(TlogCheckpointCodec.Encode(checkpoint));

        parsed.Id.Should().Be(checkpoint.Id);
        parsed.Segment.Should().Be(checkpoint.Segment);
        parsed.Sequence.Should().Be(checkpoint.Sequence);
        parsed.Count.Should().Be(checkpoint.Count);
        parsed.Hash.Should().Be(checkpoint.Hash);
        parsed.HashScheme.Should().Be(checkpoint.HashScheme);
        parsed.Kind.Should().Be(checkpoint.Kind);
        parsed.Label.Should().Be(checkpoint.Label);
        parsed.CreatedAt.Should().Be(checkpoint.CreatedAt);
        parsed.Signatures.Should().BeEquivalentTo(checkpoint.Signatures);
    }

    [Fact]
    public void Parse_reads_minimal_witness_text_with_defaults()
    {
        var rootHash = Convert.ToBase64String(Convert.FromHexString("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"));
        var text = "orders/2026\n128\n" + rootHash + "\n\n— witness.example/cosigner QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo=\n";

        var parsed = TlogCheckpointCodec.Parse(text);

        parsed.Segment.Should().Be("orders/2026");
        parsed.Count.Should().Be(128);
        parsed.Sequence.Should().Be(128);
        parsed.Hash.Should().Be("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08");
        parsed.Kind.Should().Be(AuditCheckpointKind.Regular);
        parsed.Label.Should().BeNull();
        parsed.Signatures.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("orders/2026\n128\n")]
    [InlineData("orders/2026\n0128\nQUJD\n\n")]
    [InlineData("orders/2026\n128\nnot-base64!!\n\n")]
    [InlineData("orders/2026\n128\nQUJD\n")]
    [InlineData("orders/2026\n128\nQUJD\n\n— bad+name QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo=\n")]
    [InlineData("orders/2026\r\n128\r\nQUJD\r\n\r\n")]
    public void Parse_rejects_invalid_text(string text)
    {
        var act = () => TlogCheckpointCodec.Parse(text);

        act.Should().Throw<FormatException>();
        TlogCheckpointCodec.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_returns_false_for_null()
    {
        TlogCheckpointCodec.TryParse(null, out var checkpoint).Should().BeFalse();
        checkpoint.Should().BeNull();
    }
}
