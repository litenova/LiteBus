using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Authorization.Policies;

/// <summary>
///     Verifies that each authorization policy combinator answers the verdict and code its rule names.
/// </summary>
[Collection("Sequential")]
public sealed class MessagePolicyTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that a signed-in caller passes the signed-in policy.
    /// </summary>
    [Fact]
    public async Task SignedIn_allows_an_authenticated_principal()
    {
        var verdict = await MessagePolicy.SignedIn()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that an anonymous identity fails the signed-in policy with the stable code.
    /// </summary>
    [Fact]
    public async Task SignedIn_denies_an_anonymous_identity_with_the_stable_code()
    {
        var verdict = await MessagePolicy.SignedIn()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
        AuthorizationPolicyRefusalCodes.NotSignedIn.Should().Be("authorization.not-signed-in");
    }

    /// <summary>
    ///     Verifies that a present but unauthenticated principal fails the signed-in policy.
    /// </summary>
    [Fact]
    public async Task SignedIn_denies_an_unauthenticated_principal()
    {
        var identity = ExecutionIdentity.For(new ClaimsPrincipal(new ClaimsIdentity()));

        var verdict = await MessagePolicy.SignedIn()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
    }

    /// <summary>
    ///     Verifies that a held permission passes the permission policy.
    /// </summary>
    [Fact]
    public async Task Permission_allows_when_the_lookup_grants_it()
    {
        var options = new PolicyTestOptions();
        options.Granted.Add("vaults.open");

        var verdict = await MessagePolicy.Permission("vaults.open")
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), LookupServices(options)),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a missing permission fails with the stable code.
    /// </summary>
    [Fact]
    public async Task Permission_denies_with_the_stable_code_when_the_lookup_refuses()
    {
        var options = new PolicyTestOptions();
        options.Granted.Add("vaults.read");

        var verdict = await MessagePolicy.Permission("vaults.open")
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), LookupServices(options)),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.MissingPermission);
        verdict.Reason.Should().Contain("vaults.open");
        AuthorizationPolicyRefusalCodes.MissingPermission.Should().Be("authorization.missing-permission");
    }

    /// <summary>
    ///     Verifies that a permission policy without a registered lookup denies as unconfigured.
    /// </summary>
    [Fact]
    public async Task Permission_denies_with_the_lookup_missing_code_when_none_is_registered()
    {
        var verdict = await MessagePolicy.Permission("vaults.open")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be("authorization.permission-lookup-missing");
        verdict.Reason.Should().Contain("vaults.open");
    }

    /// <summary>
    ///     Verifies that a carried claim type passes the claim policy.
    /// </summary>
    [Fact]
    public async Task Claim_allows_when_the_principal_carries_the_type()
    {
        var identity = SignedInIdentity(new Claim("department", "vaults"));

        var verdict = await MessagePolicy.Claim("department")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a carried claim with the required value passes the claim policy.
    /// </summary>
    [Fact]
    public async Task Claim_allows_when_the_value_matches()
    {
        var identity = SignedInIdentity(new Claim("department", "vaults"));

        var verdict = await MessagePolicy.Claim("department", "vaults")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a missing claim fails with the stable code.
    /// </summary>
    [Fact]
    public async Task Claim_denies_when_the_principal_lacks_the_type()
    {
        var identity = SignedInIdentity(new Claim("department", "vaults"));

        var verdict = await MessagePolicy.Claim("clearance")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
        AuthorizationPolicyRefusalCodes.Forbidden.Should().Be("authorization.forbidden");
    }

    /// <summary>
    ///     Verifies that a claim with another value fails the valued claim policy.
    /// </summary>
    [Fact]
    public async Task Claim_denies_when_the_value_differs()
    {
        var identity = SignedInIdentity(new Claim("department", "vaults"));

        var verdict = await MessagePolicy.Claim("department", "ledger")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that an anonymous caller fails the claim policy as not signed in.
    /// </summary>
    [Fact]
    public async Task Claim_denies_an_anonymous_caller_as_not_signed_in()
    {
        var verdict = await MessagePolicy.Claim("department")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
    }

    /// <summary>
    ///     Verifies that a fresh sign-in passes the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_allows_a_fresh_sign_in()
    {
        var handledAt = TestInstant();
        var identity = SignedInIdentity(SignedInAt(handledAt.AddMinutes(-5)));

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a stale sign-in fails with the stable code.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_a_stale_sign_in()
    {
        var handledAt = TestInstant();
        var identity = SignedInIdentity(SignedInAt(handledAt.AddHours(-2)));

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
        verdict.Reason.Should().Contain("00:30:00");
    }

    /// <summary>
    ///     Verifies that a principal without a sign-in instant fails the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_a_principal_without_a_sign_in_instant()
    {
        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that an anonymous caller fails the recent sign-in policy as not signed in.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_an_anonymous_caller_as_not_signed_in()
    {
        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
    }

    /// <summary>
    ///     Verifies that a run without a principal passes the system policy.
    /// </summary>
    [Fact]
    public async Task System_allows_a_run_without_a_principal()
    {
        var verdict = await MessagePolicy.System()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a call with a principal fails the system policy.
    /// </summary>
    [Fact]
    public async Task System_denies_a_call_with_a_principal()
    {
        var verdict = await MessagePolicy.System()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that the named subject passes the ownership policy.
    /// </summary>
    [Fact]
    public async Task Self_allows_the_subject_the_message_names()
    {
        var identity = SignedInIdentity(new Claim(ClaimTypes.NameIdentifier, "subject-1"));

        var verdict = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that the ownership policy reads the subject claim when no framework identifier is present.
    /// </summary>
    [Fact]
    public async Task Self_reads_the_subject_claim_without_a_framework_identifier()
    {
        var identity = SignedInIdentity(new Claim("sub", "subject-1"));

        var verdict = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that another subject fails the ownership policy.
    /// </summary>
    [Fact]
    public async Task Self_denies_a_caller_other_than_the_subject()
    {
        var identity = SignedInIdentity(new Claim(ClaimTypes.NameIdentifier, "subject-2"));

        var verdict = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that a message of another type fails the ownership policy rather than passing it.
    /// </summary>
    [Fact]
    public async Task Self_denies_a_message_of_another_type()
    {
        var verdict = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicyOtherMessage(), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that a message naming no subject fails the ownership policy.
    /// </summary>
    [Fact]
    public async Task Self_denies_a_message_naming_no_subject()
    {
        var identity = SignedInIdentity(new Claim(ClaimTypes.NameIdentifier, "subject-1"));

        var verdict = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage(null), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that an anonymous caller fails the ownership policy as not signed in.
    /// </summary>
    [Fact]
    public async Task Self_denies_an_anonymous_caller_as_not_signed_in()
    {
        var verdict = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
    }

    /// <summary>
    ///     Verifies that a conjunction allows when every inner policy allows.
    /// </summary>
    [Fact]
    public async Task All_allows_when_every_inner_policy_allows()
    {
        var verdict = await MessagePolicy.All(MessagePolicy.SignedIn(), MessagePolicy.Allow())
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a conjunction returns the first deny.
    /// </summary>
    [Fact]
    public async Task All_returns_the_first_deny()
    {
        var verdict = await MessagePolicy.All(
                MessagePolicy.Allow(),
                MessagePolicy.Deny("the first refusal"),
                MessagePolicy.Deny("the second refusal"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Reason.Should().Be("the first refusal");
    }

    /// <summary>
    ///     Verifies that a disjunction allows when one inner policy allows.
    /// </summary>
    [Fact]
    public async Task Any_allows_when_one_inner_policy_allows()
    {
        var verdict = await MessagePolicy.Any(
                MessagePolicy.Deny("the first refusal"),
                MessagePolicy.Allow())
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a disjunction returns the first deny when nothing allows.
    /// </summary>
    [Fact]
    public async Task Any_returns_the_first_deny_when_nothing_allows()
    {
        var verdict = await MessagePolicy.Any(
                MessagePolicy.Deny("the first refusal"),
                MessagePolicy.Deny("the second refusal"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Reason.Should().Be("the first refusal");
    }

    /// <summary>
    ///     Verifies that an empty disjunction denies.
    /// </summary>
    [Fact]
    public async Task Any_denies_an_empty_disjunction()
    {
        var verdict = await MessagePolicy.Any()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that a false condition skips the inner policy.
    /// </summary>
    [Fact]
    public async Task When_skips_the_inner_policy_when_the_condition_is_false()
    {
        var verdict = await MessagePolicy.When(_ => false, MessagePolicy.Deny("the inner refusal"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a true condition applies the inner policy.
    /// </summary>
    [Fact]
    public async Task When_applies_the_inner_policy_when_the_condition_holds()
    {
        var verdict = await MessagePolicy.When(_ => true, MessagePolicy.Deny("the inner refusal"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Reason.Should().Be("the inner refusal");
    }

    /// <summary>
    ///     Verifies that the allowing policy allows.
    /// </summary>
    [Fact]
    public async Task Allow_always_allows()
    {
        var verdict = await MessagePolicy.Allow()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that the denying policy denies with its reason and the stable code.
    /// </summary>
    [Fact]
    public async Task Deny_always_denies_with_the_reason()
    {
        var verdict = await MessagePolicy.Deny("the message is closed")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Reason.Should().Be("the message is closed");
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that the refusal codes carry the documented values.
    /// </summary>
    [Fact]
    public void Refusal_codes_carry_the_documented_values()
    {
        AuthorizationPolicyRefusalCodes.NotSignedIn.Should().Be("authorization.not-signed-in");
        AuthorizationPolicyRefusalCodes.MissingPermission.Should().Be("authorization.missing-permission");
        AuthorizationPolicyRefusalCodes.Forbidden.Should().Be("authorization.forbidden");
        AuthorizationPolicyRefusalCodes.UnknownPolicy.Should().Be("authorization.unknown-policy");
    }

    /// <summary>
    ///     Verifies that a blank permission is rejected where it is written.
    /// </summary>
    /// <param name="permission">The blank permission under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_permission_is_rejected_where_it_is_written(string? permission)
    {
        var act = () => MessagePolicy.Permission(permission!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that a blank claim type is rejected where it is written.
    /// </summary>
    /// <param name="type">The blank claim type under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_claim_type_is_rejected_where_it_is_written(string? type)
    {
        var act = () => MessagePolicy.Claim(type!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that a negative sign-in age is rejected.
    /// </summary>
    [Fact]
    public void A_negative_max_age_is_rejected()
    {
        var act = () => MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(-1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies that a null subject selector is rejected.
    /// </summary>
    [Fact]
    public void A_null_subject_selector_is_rejected()
    {
        var act = () => MessagePolicy.Self<PolicySubjectMessage>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies that null inner policies are rejected where the combinator is built.
    /// </summary>
    [Fact]
    public void Null_inner_policies_are_rejected()
    {
        var nullList = () => MessagePolicy.All(null!);
        var nullElement = () => MessagePolicy.Any(MessagePolicy.Allow(), null!);

        nullList.Should().Throw<ArgumentNullException>();
        nullElement.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that null conditional arguments are rejected.
    /// </summary>
    [Fact]
    public void Null_conditional_arguments_are_rejected()
    {
        var nullCondition = () => MessagePolicy.When(null!, MessagePolicy.Allow());
        var nullInner = () => MessagePolicy.When(_ => true, null!);

        nullCondition.Should().Throw<ArgumentNullException>();
        nullInner.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies that a blank deny reason is rejected where it is written.
    /// </summary>
    /// <param name="reason">The blank reason under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_deny_reason_is_rejected_where_it_is_written(string? reason)
    {
        var act = () => MessagePolicy.Deny(reason!);

        act.Should().Throw<ArgumentException>();
    }

    private static AuthorizationContext ContextFor(
        object message,
        ExecutionIdentity identity,
        IServiceProvider? services = null,
        DateTimeOffset? handledAt = null)
    {
        return new AuthorizationContext(
            message,
            message.GetType(),
            identity,
            services ?? new ServiceCollection().BuildServiceProvider(),
            handledAt ?? TestInstant());
    }

    private static ExecutionIdentity SignedInIdentity(params Claim[] claims)
    {
        return ExecutionIdentity.For(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
    }

    private static Claim SignedInAt(DateTimeOffset instant)
    {
        return new Claim("auth_time", instant.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    private static DateTimeOffset TestInstant()
    {
        return new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    }

    private static IServiceProvider LookupServices(PolicyTestOptions options)
    {
        return new ServiceCollection()
            .AddSingleton(options)
            .AddSingleton<IPermissionLookup, FakePermissionLookup>()
            .BuildServiceProvider();
    }
}
