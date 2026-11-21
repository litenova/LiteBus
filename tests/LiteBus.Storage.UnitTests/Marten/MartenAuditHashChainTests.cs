using LiteBus.Auditing.Storage.Marten;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Storage.UnitTests.Marten;

/// <summary>
///     Verifies the Marten audit hash chain: determinism, predecessor binding, and field sensitivity.
/// </summary>
public sealed class MartenAuditHashChainTests
{
    [Fact]
    public void Same_document_hashes_the_same_twice()
    {
        var record = ChainedDocument();

        MartenAuditHashChain.ComputeHash(record).Should().Be(MartenAuditHashChain.ComputeHash(record));
    }

    [Fact]
    public void Hash_is_a_64_character_lowercase_hexadecimal_string()
    {
        var hash = MartenAuditHashChain.ComputeHash(ChainedDocument());

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Different_predecessor_hash_changes_the_hash()
    {
        var first = ChainedDocument();
        var second = ChainedDocument();
        second.PreviousHash = "another-predecessor-hash";

        MartenAuditHashChain.ComputeHash(first).Should().NotBe(MartenAuditHashChain.ComputeHash(second));
    }

    [Fact]
    public void Different_sequence_changes_the_hash()
    {
        var first = ChainedDocument();
        var second = ChainedDocument();
        second.Sequence = first.Sequence + 1;

        MartenAuditHashChain.ComputeHash(first).Should().NotBe(MartenAuditHashChain.ComputeHash(second));
    }

    [Fact]
    public void Altered_action_changes_the_hash()
    {
        var first = ChainedDocument();
        var second = ChainedDocument();
        second.Action = "orders.cancel-order";

        MartenAuditHashChain.ComputeHash(first).Should().NotBe(MartenAuditHashChain.ComputeHash(second));
    }

    [Fact]
    public void Altered_property_changes_the_hash()
    {
        var first = ChainedDocument();
        var second = ChainedDocument();
        second.Properties["order-total"] = "altered";

        MartenAuditHashChain.ComputeHash(first).Should().NotBe(MartenAuditHashChain.ComputeHash(second));
    }

    [Fact]
    public void Property_insertion_order_does_not_change_the_hash()
    {
        var first = ChainedDocument();
        var second = ChainedDocument();

        // The same entries in the opposite insertion order: every other field stays identical.
        second.Properties.Clear();
        second.Properties["warehouse"] = "amsterdam";
        second.Properties["order-total"] = "9.50";

        MartenAuditHashChain.ComputeHash(first).Should().Be(MartenAuditHashChain.ComputeHash(second));
    }

    [Fact]
    public void Stored_hash_does_not_change_the_hash()
    {
        var first = ChainedDocument();
        var second = ChainedDocument();
        second.Hash = "whatever-is-stored";

        // The stored hash is what is being computed, so it is excluded from the canonical form.
        MartenAuditHashChain.ComputeHash(first).Should().Be(MartenAuditHashChain.ComputeHash(second));
    }

    [Fact]
    public void Null_record_is_rejected()
    {
        var hash = () => MartenAuditHashChain.ComputeHash(null!);
        hash.Should().Throw<ArgumentNullException>();
    }

    private static MartenAuditRecord ChainedDocument()
    {
        return new MartenAuditRecord
        {
            Id = Guid.Parse("d3a4b2c1-8f6e-4a1b-9c2d-5e7f0a1b3c4d"),
            Action = "orders.place-order",
            ActorId = "account-7",
            ActorKind = AuditActor.UserKind,
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Duration = TimeSpan.FromMilliseconds(12),
            Category = "orders",
            TargetKind = "order",
            TargetId = "order-9",
            CorrelationId = "correlation-1",
            TenantId = "tenant-a",
            Segment = "orders/2026",
            Sequence = 3,
            PreviousHash = "predecessor-hash",
            Properties = new Dictionary<string, string>
            {
                ["order-total"] = "9.50",
                ["warehouse"] = "amsterdam"
            }
        };
    }
}
