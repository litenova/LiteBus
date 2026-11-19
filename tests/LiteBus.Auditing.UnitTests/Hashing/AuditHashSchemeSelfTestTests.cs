using System.Text;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Hashing;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.UnitTests.Hashing;

/// <summary>
///     Verifies that the hash scheme self-test accepts bound schemes and rejects misbound ones at composition.
/// </summary>
public sealed class AuditHashSchemeSelfTestTests
{
    [Fact]
    public void The_framed_rule_passes_the_position_check()
    {
        var act = () => AuditHashSchemeSelfTest.VerifyPositionBinding("sha256-v2", new FramedLinkRule("sha256-v2"));

        act.Should().NotThrow();
    }

    [Fact]
    public void A_pass_through_rule_fails_the_position_check()
    {
        var act = () => AuditHashSchemeSelfTest.VerifyPositionBinding(
            "custom-v1", new LiteBus.Auditing.Hashing.PreviousHashLinkRule());

        act.Should().Throw<LiteBusConfigurationException>().WithMessage("*custom-v1*");
    }

    [Fact]
    public void A_rule_binding_no_previous_hash_fails_on_the_previous_hash()
    {
        var act = () => AuditHashSchemeSelfTest.VerifyPositionBinding("custom-v1", new SegmentSequenceLinkRule());

        act.Should().Throw<LiteBusConfigurationException>().WithMessage("*previous hash*");
    }

    [Fact]
    public void A_blank_scheme_name_is_rejected_by_the_position_check()
    {
        var act = () => AuditHashSchemeSelfTest.VerifyPositionBinding("  ", new FramedLinkRule());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_null_rule_is_rejected_by_the_position_check()
    {
        var act = () => AuditHashSchemeSelfTest.VerifyPositionBinding("sha256-v2", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void The_framed_rule_factory_passes_the_scheme_check()
    {
        var act = () => AuditHashSchemeSelfTest.VerifySchemeBinding(
            "sha256-v2", "hmac-sha256-v2", name => new FramedLinkRule(name));

        act.Should().NotThrow();
    }

    [Fact]
    public void A_factory_ignoring_the_scheme_fails_the_scheme_check()
    {
        var act = () => AuditHashSchemeSelfTest.VerifySchemeBinding(
            "sha256-v2", "hmac-sha256-v2", _ => new LiteBus.Auditing.Hashing.PreviousHashLinkRule());

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*sha256-v2*")
            .WithMessage("*hmac-sha256-v2*");
    }

    [Fact]
    public void Equal_scheme_names_are_rejected_by_the_scheme_check()
    {
        var act = () => AuditHashSchemeSelfTest.VerifySchemeBinding(
            "sha256-v2", "sha256-v2", name => new FramedLinkRule(name));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_framed_rule_passes_the_hash_check()
    {
        await AuditHashSchemeSelfTest.VerifyHashBindingAsync(
                "sha256-v2", new FramedLinkRule("sha256-v2"), new Sha256AuditHasher(), CancellationToken.None)
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task A_pass_through_rule_fails_the_hash_check()
    {
        var act = () => AuditHashSchemeSelfTest.VerifyHashBindingAsync(
                "custom-v1", new LiteBus.Auditing.Hashing.PreviousHashLinkRule(), new Sha256AuditHasher(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<LiteBusConfigurationException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task A_rule_binding_no_previous_hash_fails_the_hash_check_on_the_previous_hash()
    {
        var act = () => AuditHashSchemeSelfTest.VerifyHashBindingAsync(
                "custom-v1", new SegmentSequenceLinkRule(), new Sha256AuditHasher(), CancellationToken.None)
            .AsTask();

        await act.Should().ThrowAsync<LiteBusConfigurationException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task A_cancelled_token_throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(false);

        var act = () => AuditHashSchemeSelfTest.VerifyHashBindingAsync(
                "sha256-v2", new FramedLinkRule(), new Sha256AuditHasher(), cancelled.Token)
            .AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     A link rule that binds the segment and sequence but leaves the previous hash out, standing in for a rule
    ///     that frames the position incompletely.
    /// </summary>
    private sealed class SegmentSequenceLinkRule : IAuditLinkRule
    {
        /// <inheritdoc />
        public byte[] Link(AuditLinkInput input)
        {
            ArgumentNullException.ThrowIfNull(input);

            return [.. Encoding.UTF8.GetBytes(string.Concat(input.Segment, input.Sequence)), .. input.CanonicalBytes];
        }
    }
}
