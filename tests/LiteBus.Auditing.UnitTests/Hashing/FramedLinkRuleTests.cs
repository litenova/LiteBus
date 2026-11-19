using System.Buffers.Binary;
using LiteBus.Auditing.Abstractions;
using System.Text;
using LiteBus.Auditing.Hashing;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.UnitTests.Hashing;

/// <summary>
///     Verifies that the framed link rule binds the scheme, segment, sequence, and previous hash around the canonical
///     business bytes.
/// </summary>
public sealed class FramedLinkRuleTests
{
    [Fact]
    public void The_default_rule_frames_the_current_shipped_scheme()
    {
        var rule = new FramedLinkRule();

        rule.SchemeName.Should().Be(AuditHashSchemeNames.Sha256V2);
    }

    [Fact]
    public void A_blank_scheme_name_is_rejected()
    {
        var act = () => new FramedLinkRule("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_null_input_is_rejected()
    {
        var rule = new FramedLinkRule();

        var act = () => rule.Link(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void The_same_input_links_to_the_same_bytes()
    {
        var rule = new FramedLinkRule();
        var input = new AuditLinkInput("orders/2026", 2, "previous", [1, 2, 3, 4]);

        var first = rule.Link(input);
        var second = rule.Link(input);

        second.Should().Equal(first);
    }

    [Fact]
    public void The_linked_bytes_carry_the_length_prefixed_frame_followed_by_the_canonical_bytes()
    {
        var rule = new FramedLinkRule("sha256-v2");
        byte[] canonical = [1, 2, 3, 4];
        var input = new AuditLinkInput("orders/2026", 2, "previous", canonical);

        var linked = rule.Link(input);

        byte[] expected =
        [
            .. LengthPrefixed("sha256-v2"),
            .. LengthPrefixed("orders/2026"),
            .. SequenceBytes(2),
            .. LengthPrefixed("previous"),
            .. canonical,
        ];

        linked.Should().Equal(expected);
    }

    [Fact]
    public void A_missing_previous_hash_frames_differently_from_an_empty_one()
    {
        var rule = new FramedLinkRule();
        var missing = new AuditLinkInput("orders/2026", 1, null, [1, 2, 3, 4]);
        var empty = new AuditLinkInput("orders/2026", 1, string.Empty, [1, 2, 3, 4]);

        var missingLinked = rule.Link(missing);
        var emptyLinked = rule.Link(empty);

        missingLinked.Should().NotEqual(emptyLinked);
    }

    [Fact]
    public void A_missing_previous_hash_writes_the_null_marker()
    {
        var rule = new FramedLinkRule("sha256-v2");
        var input = new AuditLinkInput("orders/2026", 1, null, [9]);

        var linked = rule.Link(input);

        byte[] expected =
        [
            .. LengthPrefixed("sha256-v2"),
            .. LengthPrefixed("orders/2026"),
            .. SequenceBytes(1),
            0xFF, 0xFF, 0xFF, 0xFF,
            9,
        ];

        linked.Should().Equal(expected);
    }

    [Fact]
    public void A_different_scheme_frames_differently()
    {
        var input = new AuditLinkInput("orders/2026", 2, "previous", [1, 2, 3, 4]);

        var first = new FramedLinkRule("sha256-v2").Link(input);
        var second = new FramedLinkRule("hmac-sha256-v2").Link(input);

        second.Should().NotEqual(first);
    }

    [Fact]
    public void A_different_segment_frames_differently()
    {
        var rule = new FramedLinkRule();

        var first = rule.Link(new AuditLinkInput("orders/2026", 2, "previous", [1, 2, 3, 4]));
        var second = rule.Link(new AuditLinkInput("orders/2027", 2, "previous", [1, 2, 3, 4]));

        second.Should().NotEqual(first);
    }

    [Fact]
    public void A_different_sequence_frames_differently()
    {
        var rule = new FramedLinkRule();

        var first = rule.Link(new AuditLinkInput("orders/2026", 2, "previous", [1, 2, 3, 4]));
        var second = rule.Link(new AuditLinkInput("orders/2026", 3, "previous", [1, 2, 3, 4]));

        second.Should().NotEqual(first);
    }

    [Fact]
    public void A_different_previous_hash_frames_differently()
    {
        var rule = new FramedLinkRule();

        var first = rule.Link(new AuditLinkInput("orders/2026", 2, "previous", [1, 2, 3, 4]));
        var second = rule.Link(new AuditLinkInput("orders/2026", 2, "other", [1, 2, 3, 4]));

        second.Should().NotEqual(first);
    }

    [Fact]
    public void Different_canonical_bytes_frame_differently()
    {
        var rule = new FramedLinkRule();

        var first = rule.Link(new AuditLinkInput("orders/2026", 2, "previous", [1, 2, 3, 4]));
        var second = rule.Link(new AuditLinkInput("orders/2026", 2, "previous", [1, 2, 3, 5]));

        second.Should().NotEqual(first);
    }

    [Fact]
    public void Adjacent_fields_cannot_merge_across_their_boundary()
    {
        // Without length prefixes, "ab" + "c" and "a" + "bc" concatenate to the same bytes.
        var first = new FramedLinkRule("ab").Link(new AuditLinkInput("c", 2, "previous", [1]));
        var second = new FramedLinkRule("a").Link(new AuditLinkInput("bc", 2, "previous", [1]));

        second.Should().NotEqual(first);
    }

    /// <summary>
    ///     Builds the expected encoding of one framed text field.
    /// </summary>
    /// <param name="value">The field value.</param>
    /// <returns>The four-byte big-endian length followed by the UTF-8 bytes.</returns>
    private static byte[] LengthPrefixed(string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        byte[] prefixed = new byte[4 + encoded.Length];

        BinaryPrimitives.WriteUInt32BigEndian(prefixed.AsSpan(0, 4), unchecked((uint) encoded.Length));
        encoded.CopyTo(prefixed, 4);

        return prefixed;
    }

    /// <summary>
    ///     Builds the expected encoding of one framed sequence.
    /// </summary>
    /// <param name="sequence">The sequence.</param>
    /// <returns>The sequence as eight bytes, big-endian.</returns>
    private static byte[] SequenceBytes(long sequence)
    {
        byte[] encoded = new byte[8];

        BinaryPrimitives.WriteUInt64BigEndian(encoded, unchecked((ulong) sequence));

        return encoded;
    }
}
