using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Verification;
using LiteBus.Auditing.Hashing;

namespace LiteBus.Auditing.UnitTests.Chain;

/// <summary>
///     Verifies that the default audit link rule hashes canonical bytes unchanged.
/// </summary>
public sealed class PreviousHashLinkRuleTests
{
    [Fact]
    public void The_canonical_bytes_pass_through_unchanged()
    {
        var rule = new PreviousHashLinkRule();
        byte[] canonical = [1, 2, 3, 4];
        var input = new AuditLinkInput("orders/2026", 2, "previous", canonical);

        var linked = rule.Link(input);

        linked.Should().BeSameAs(canonical);
    }

    [Fact]
    public void A_null_input_is_rejected()
    {
        var rule = new PreviousHashLinkRule();

        var act = () => rule.Link(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
