using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.UnitTests.Verification;

/// <summary>
///     Verifies that chain breaks report which question they answer and that a verification separates integrity from
///     coverage.
/// </summary>
public sealed class AuditChainBreakCategoryTests
{
    /// <summary>
    ///     Every break kind maps to the category its summary describes.
    /// </summary>
    /// <param name="kind">The break kind under test.</param>
    /// <param name="expected">The category the kind maps to.</param>
    [Theory]
    [InlineData(AuditChainBreakKind.Gap, AuditChainBreakCategory.Integrity)]
    [InlineData(AuditChainBreakKind.Duplicate, AuditChainBreakCategory.Integrity)]
    [InlineData(AuditChainBreakKind.PreviousHashMismatch, AuditChainBreakCategory.Integrity)]
    [InlineData(AuditChainBreakKind.HashMismatch, AuditChainBreakCategory.Integrity)]
    [InlineData(AuditChainBreakKind.UnknownHashScheme, AuditChainBreakCategory.Integrity)]
    [InlineData(AuditChainBreakKind.CheckpointHashMismatch, AuditChainBreakCategory.Integrity)]
    [InlineData(AuditChainBreakKind.CheckpointCountMismatch, AuditChainBreakCategory.Integrity)]
    [InlineData(AuditChainBreakKind.CheckpointSignatureInvalid, AuditChainBreakCategory.Integrity)]
    [InlineData(AuditChainBreakKind.CheckpointMissing, AuditChainBreakCategory.Coverage)]
    [InlineData(AuditChainBreakKind.CheckpointStale, AuditChainBreakCategory.Coverage)]
    [InlineData(AuditChainBreakKind.CheckpointUnpublished, AuditChainBreakCategory.Coverage)]
    [InlineData(AuditChainBreakKind.Unlinked, AuditChainBreakCategory.Coverage)]
    public void Every_break_kind_has_its_category(AuditChainBreakKind kind, AuditChainBreakCategory expected)
    {
        var item = new AuditChainBreak(1, kind, "A break.");

        item.Category.Should().Be(expected);
    }

    /// <summary>
    ///     A verification holding only coverage breaks leaves the chain intact.
    /// </summary>
    [Fact]
    public void Coverage_breaks_leave_the_chain_intact()
    {
        var verification = new AuditChainVerification
        {
            Segment = "orders/2026",
            RecordCount = 3,
            Breaks =
            [
                new AuditChainBreak(1, AuditChainBreakKind.CheckpointMissing, "No checkpoint."),
                new AuditChainBreak(2, AuditChainBreakKind.CheckpointStale, "Stale checkpoint."),
                new AuditChainBreak(3, AuditChainBreakKind.Unlinked, "Unlinked record."),
            ],
        };

        verification.IsIntact.Should().BeFalse();
        verification.IsChainIntact.Should().BeTrue();
    }

    /// <summary>
    ///     A verification holding an integrity break reports the chain as not intact.
    /// </summary>
    [Fact]
    public void An_integrity_break_makes_the_chain_not_intact()
    {
        var verification = new AuditChainVerification
        {
            Segment = "orders/2026",
            RecordCount = 3,
            Breaks =
            [
                new AuditChainBreak(1, AuditChainBreakKind.CheckpointMissing, "No checkpoint."),
                new AuditChainBreak(2, AuditChainBreakKind.HashMismatch, "Altered document."),
            ],
        };

        verification.IsChainIntact.Should().BeFalse();
    }
}
