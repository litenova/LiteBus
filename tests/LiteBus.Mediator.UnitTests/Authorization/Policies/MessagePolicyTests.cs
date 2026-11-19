using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Abstractions.Tenancy;
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
    ///     Verifies that a mediation running in a tenant passes the tenant policy.
    /// </summary>
    [Fact]
    public async Task Tenant_allows_when_the_mediation_runs_in_a_tenant()
    {
        var verdict = await MessagePolicy.Tenant()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), TenantIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a mediation running in no tenant fails the tenant policy with the stable code.
    /// </summary>
    [Fact]
    public async Task Tenant_denies_without_a_tenant_with_the_stable_code()
    {
        var verdict = await MessagePolicy.Tenant()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(TenancyRefusalCodes.TenantRequired);
        verdict.Reason.Should().Contain("PolicySubjectMessage");
        TenancyRefusalCodes.TenantRequired.Should().Be("tenancy.required");
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
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.PermissionLookupMissing);
        verdict.Reason.Should().Contain("vaults.open");
        AuthorizationPolicyRefusalCodes.PermissionLookupMissing.Should().Be("authorization.permission-lookup-missing");
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
    ///     Verifies that a principal without a sign-in instant fails the recent sign-in policy with the stable code.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_a_principal_without_a_sign_in_instant()
    {
        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
        AuthorizationPolicyRefusalCodes.SignInInstantUnknown.Should().Be("authorization.sign-in-instant-unknown");
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
    ///     Verifies that an unparsable sign-in instant fails the recent sign-in policy with the stable code.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_an_unparsable_sign_in_instant()
    {
        var identity = SignedInIdentity(new Claim("auth_time", "not-a-number"));

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
    }

    /// <summary>
    ///     Verifies that a sign-in instant outside the representable range fails the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_an_out_of_range_sign_in_instant()
    {
        var identity = SignedInIdentity(new Claim("auth_time", long.MaxValue.ToString(CultureInfo.InvariantCulture)));

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
    }

    /// <summary>
    ///     Verifies that a fresh sign-in read from the registered source passes the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_allows_a_fresh_sign_in_from_the_instant_source()
    {
        var handledAt = TestInstant();

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    SourceServices(new FixedSignInInstantSource(handledAt.AddMinutes(-5))),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a stale sign-in read from the registered source fails the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_a_stale_sign_in_from_the_instant_source()
    {
        var handledAt = TestInstant();

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    SourceServices(new FixedSignInInstantSource(handledAt.AddHours(-2))),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that an unknown instant from the registered source fails the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_when_the_source_returns_no_instant()
    {
        var handledAt = TestInstant();

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(SignedInAt(handledAt.AddMinutes(-5))),
                    SourceServices(new FixedSignInInstantSource(null)),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
    }

    /// <summary>
    ///     Verifies that the registered source wins over the claim when both carry an instant.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_prefers_the_source_over_the_claim()
    {
        var handledAt = TestInstant();
        var identity = SignedInIdentity(SignedInAt(handledAt.AddHours(-2)));

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    identity,
                    SourceServices(new FixedSignInInstantSource(handledAt.AddMinutes(-5))),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a sign-in older than a sub-second window fails, proving the age comparison keeps
    ///     sub-second precision.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_a_sign_in_older_than_a_sub_second_window()
    {
        var handledAt = TestInstant();

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMilliseconds(500))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    SourceServices(new FixedSignInInstantSource(handledAt.AddMilliseconds(-600))),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that a sign-in within a sub-second window passes.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_allows_a_sign_in_within_a_sub_second_window()
    {
        var handledAt = TestInstant();

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMilliseconds(500))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    SourceServices(new FixedSignInInstantSource(handledAt.AddMilliseconds(-400))),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that the claim overload reads the configured claim type.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_reads_the_configured_claim_type()
    {
        var handledAt = TestInstant();
        var identity = SignedInIdentity(CustomSignedInAt("session_auth_time", handledAt.AddMinutes(-5)));

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30), "session_auth_time")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that the claim overload ignores the default claim when another type is configured.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_ignores_other_claims_when_a_claim_type_is_configured()
    {
        var handledAt = TestInstant();
        var identity = SignedInIdentity(SignedInAt(handledAt.AddMinutes(-5)));

        var verdict = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30), "session_auth_time")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
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
    ///     Verifies that the public policy allows without a caller.
    /// </summary>
    [Fact]
    public async Task Public_allows_without_a_caller()
    {
        var verdict = await MessagePolicy.Public()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that the public policy allows a signed-in caller.
    /// </summary>
    [Fact]
    public async Task Public_allows_a_signed_in_caller()
    {
        var verdict = await MessagePolicy.Public()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
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
    ///     Verifies that a registered condition answers the verdict the condition policy returns.
    /// </summary>
    [Fact]
    public async Task Condition_answers_the_registered_condition_verdict()
    {
        var verdict = await MessagePolicy.Condition<FixedPolicyCondition>()
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    ConditionServices(new FixedPolicyCondition(Verdict.Allow))),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a denying condition refuses through the condition policy with its own code.
    /// </summary>
    [Fact]
    public async Task Condition_returns_the_condition_denial()
    {
        var verdict = await MessagePolicy.Condition<FixedPolicyCondition>()
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    ConditionServices(new FixedPolicyCondition(Verdict.Deny("the condition refused.", "test.condition")))),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Reason.Should().Be("the condition refused.");
        verdict.Code.Should().Be("test.condition");
    }

    /// <summary>
    ///     Verifies that a condition policy without a registered condition denies with the stable code.
    /// </summary>
    [Fact]
    public async Task Condition_denies_with_the_stable_code_when_none_is_registered()
    {
        var verdict = await MessagePolicy.Condition<FixedPolicyCondition>()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.ConditionMissing);
        verdict.Reason.Should().Contain(nameof(FixedPolicyCondition));
        AuthorizationPolicyRefusalCodes.ConditionMissing.Should().Be("authorization.condition-missing");
    }

    /// <summary>
    ///     Verifies that a nested mediation whose parent handles the named message passes the sent-by policy.
    /// </summary>
    [Fact]
    public async Task SentBy_allows_when_the_parent_handles_the_named_message()
    {
        var parent = new PolicyExecutionContext(messageType: typeof(PolicySubjectMessage));
        var current = new PolicyExecutionContext(parent: parent);

        var verdict = await MessagePolicy.SentBy<PolicySubjectMessage>()
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), executionContext: current),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a nested mediation whose parent message derives from the named type passes the sent-by
    ///     policy.
    /// </summary>
    [Fact]
    public async Task SentBy_allows_when_the_parent_message_is_assignable_to_the_named_type()
    {
        var parent = new PolicyExecutionContext(messageType: typeof(PolicySubjectMessage));
        var current = new PolicyExecutionContext(parent: parent);

        var verdict = await MessagePolicy.SentBy<object>()
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), executionContext: current),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a mediation with no execution context fails the sent-by policy with the stable code.
    /// </summary>
    [Fact]
    public async Task SentBy_denies_without_an_execution_context()
    {
        var verdict = await MessagePolicy.SentBy<PolicySubjectMessage>()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSentBy);
        AuthorizationPolicyRefusalCodes.NotSentBy.Should().Be("authorization.not-sent-by");
    }

    /// <summary>
    ///     Verifies that a root mediation fails the sent-by policy.
    /// </summary>
    [Fact]
    public async Task SentBy_denies_a_root_mediation()
    {
        var current = new PolicyExecutionContext(messageType: typeof(PolicySubjectMessage));

        var verdict = await MessagePolicy.SentBy<PolicySubjectMessage>()
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), executionContext: current),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSentBy);
    }

    /// <summary>
    ///     Verifies that a nested mediation whose parent handles another message fails the sent-by policy.
    /// </summary>
    [Fact]
    public async Task SentBy_denies_when_the_parent_handles_another_message()
    {
        var parent = new PolicyExecutionContext(messageType: typeof(PolicyOtherMessage));
        var current = new PolicyExecutionContext(parent: parent);

        var verdict = await MessagePolicy.SentBy<PolicySubjectMessage>()
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), executionContext: current),
                CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSentBy);
        verdict.Reason.Should().Contain(nameof(PolicySubjectMessage));
    }

    /// <summary>
    ///     Verifies that the refusal wrapper replaces the code and reason of a denial.
    /// </summary>
    [Fact]
    public async Task WithRefusal_replaces_the_code_and_reason_of_a_denial()
    {
        var verdict = await MessagePolicy.Deny("the inner refusal").WithRefusal("product.closed", "the product refusal")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be("product.closed");
        verdict.Reason.Should().Be("the product refusal");
    }

    /// <summary>
    ///     Verifies that the refusal wrapper keeps the inner reason when none is given.
    /// </summary>
    [Fact]
    public async Task WithRefusal_keeps_the_inner_reason_when_none_is_given()
    {
        var verdict = await MessagePolicy.Deny("the inner refusal").WithRefusal("product.closed")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeTrue();
        verdict.Code.Should().Be("product.closed");
        verdict.Reason.Should().Be("the inner refusal");
    }

    /// <summary>
    ///     Verifies that the refusal wrapper passes an allow through unchanged.
    /// </summary>
    [Fact]
    public async Task WithRefusal_passes_an_allow_through()
    {
        var verdict = await MessagePolicy.Allow().WithRefusal("product.closed", "the product refusal")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        verdict.IsDenied.Should().BeFalse();
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
        AuthorizationPolicyRefusalCodes.PermissionLookupMissing.Should().Be("authorization.permission-lookup-missing");
        AuthorizationPolicyRefusalCodes.ConditionMissing.Should().Be("authorization.condition-missing");
        AuthorizationPolicyRefusalCodes.SignInInstantUnknown.Should().Be("authorization.sign-in-instant-unknown");
        AuthorizationPolicyRefusalCodes.NotSentBy.Should().Be("authorization.not-sent-by");
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

    /// <summary>
    ///     Verifies that a blank refusal code is rejected where the wrapper is written.
    /// </summary>
    /// <param name="code">The blank code under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_refusal_code_is_rejected_where_it_is_written(string? code)
    {
        var act = () => MessagePolicy.Deny("the inner refusal").WithRefusal(code!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that a blank refusal reason is rejected where the wrapper is written.
    /// </summary>
    /// <param name="reason">The blank reason under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_refusal_reason_is_rejected_where_it_is_written(string? reason)
    {
        var act = () => MessagePolicy.Deny("the inner refusal").WithRefusal("product.closed", reason);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that a blank sign-in claim type is rejected where it is written.
    /// </summary>
    /// <param name="claimType">The blank claim type under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_sign_in_claim_type_is_rejected_where_it_is_written(string? claimType)
    {
        var act = () => MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30), claimType!);

        act.Should().Throw<ArgumentException>();
    }

    private static AuthorizationContext ContextFor(
        object message,
        ExecutionIdentity identity,
        IServiceProvider? services = null,
        DateTimeOffset? handledAt = null,
        IExecutionContext? executionContext = null)
    {
        return new AuthorizationContext(
            message,
            message.GetType(),
            identity,
            services ?? new ServiceCollection().BuildServiceProvider(),
            handledAt ?? TestInstant())
        {
            ExecutionContext = executionContext
        };
    }

    private static ExecutionIdentity SignedInIdentity(params Claim[] claims)
    {
        return ExecutionIdentity.For(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
    }

    private static ExecutionIdentity TenantIdentity(params Claim[] claims)
    {
        return ExecutionIdentity.For(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), "tenant-1");
    }

    private static Claim SignedInAt(DateTimeOffset instant)
    {
        return new Claim("auth_time", instant.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    private static Claim CustomSignedInAt(string claimType, DateTimeOffset instant)
    {
        return new Claim(claimType, instant.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
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

    private static IServiceProvider ConditionServices(FixedPolicyCondition condition)
    {
        return new ServiceCollection()
            .AddSingleton(condition)
            .BuildServiceProvider();
    }

    private static IServiceProvider SourceServices(ISignInInstantSource source)
    {
        return new ServiceCollection()
            .AddSingleton(source)
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Answers a fixed verdict, so the condition policy tests control what the named condition decides.
    /// </summary>
    private sealed class FixedPolicyCondition : IAuthorizationCondition
    {
        /// <summary>
        ///     The verdict this condition answers.
        /// </summary>
        private readonly Verdict _verdict;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedPolicyCondition" /> class.
        /// </summary>
        /// <param name="verdict">The verdict this condition answers.</param>
        public FixedPolicyCondition(Verdict verdict)
        {
            _verdict = verdict;
        }

        /// <inheritdoc />
        public ValueTask<Verdict> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<Verdict>(_verdict);
        }
    }

    /// <summary>
    ///     Answers a fixed sign-in instant, so the recent sign-in tests control what the source reads.
    /// </summary>
    private sealed class FixedSignInInstantSource : ISignInInstantSource
    {
        /// <summary>
        ///     The instant this source answers, or <see langword="null" /> when it is unknown.
        /// </summary>
        private readonly DateTimeOffset? _instant;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedSignInInstantSource" /> class.
        /// </summary>
        /// <param name="instant">The instant this source answers, or <see langword="null" /> when it is unknown.</param>
        public FixedSignInInstantSource(DateTimeOffset? instant)
        {
            _instant = instant;
        }

        /// <inheritdoc />
        public ValueTask<DateTimeOffset?> GetSignInInstantAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<DateTimeOffset?>(_instant);
        }
    }

    /// <summary>
    ///     Carries a chosen message type and parent, so the sent-by policy tests control the mediation lineage.
    /// </summary>
    private sealed class PolicyExecutionContext : IExecutionContext
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="PolicyExecutionContext" /> class.
        /// </summary>
        /// <param name="messageType">The runtime type of the message this mediation handles.</param>
        /// <param name="parent">The execution context of the enclosing mediation.</param>
        public PolicyExecutionContext(Type? messageType = null, IExecutionContext? parent = null)
        {
            MessageType = messageType;
            Parent = parent;
        }

        /// <inheritdoc />
        public CancellationToken CancellationToken => CancellationToken.None;

        /// <inheritdoc />
        public DateTimeOffset HandledAt { get; } = DateTimeOffset.UtcNow;

        /// <inheritdoc />
        public Type? MessageType { get; }

        /// <inheritdoc />
        public IExecutionContext? Parent { get; }

        /// <inheritdoc />
        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

        /// <inheritdoc />
        public IHandleContextData Data { get; } = new HandleContextData();

        /// <inheritdoc />
        public IReadOnlyCollection<string> Tags { get; } = [];

        /// <inheritdoc />
        public object? MessageResult { get; set; }

        /// <inheritdoc />
        public bool PostHandlersSuppressed { get; private set; }

        /// <inheritdoc />
        public void SuppressPostHandlers()
        {
            PostHandlersSuppressed = true;
        }
    }
}
