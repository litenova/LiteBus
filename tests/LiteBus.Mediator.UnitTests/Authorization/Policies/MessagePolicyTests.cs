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
///     Verifies that each authorization policy combinator answers the result its rule names.
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
        var result = await MessagePolicy.SignedIn()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that an anonymous identity fails the signed-in policy with the stable code.
    /// </summary>
    [Fact]
    public async Task SignedIn_denies_an_anonymous_identity_with_the_stable_code()
    {
        var result = await MessagePolicy.SignedIn()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
        AuthorizationPolicyRefusalCodes.NotSignedIn.Should().Be("authorization.not-signed-in");
    }

    /// <summary>
    ///     Verifies that a present but unauthenticated principal fails the signed-in policy.
    /// </summary>
    [Fact]
    public async Task SignedIn_denies_an_unauthenticated_principal()
    {
        var identity = ExecutionIdentity.For(new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await MessagePolicy.SignedIn()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
    }

    /// <summary>
    ///     Verifies that a mediation running in a tenant passes the tenant policy.
    /// </summary>
    [Fact]
    public async Task Tenant_allows_when_the_mediation_runs_in_a_tenant()
    {
        var result = await MessagePolicy.Tenant()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), TenantIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a mediation running in no tenant fails the tenant policy with the stable code.
    /// </summary>
    [Fact]
    public async Task Tenant_denies_without_a_tenant_with_the_stable_code()
    {
        var result = await MessagePolicy.Tenant()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(TenancyRefusalCodes.TenantRequired);
        deny.Reason.Should().Contain("PolicySubjectMessage");
        TenancyRefusalCodes.TenantRequired.Should().Be("tenancy.required");
    }

    /// <summary>
    ///     Verifies that a tenant the registered validator accepts passes the tenant policy.
    /// </summary>
    [Fact]
    public async Task Tenant_allows_a_tenant_the_validator_accepts()
    {
        var validator = new FixedTenantValidator(valid: true);

        var result = await MessagePolicy.Tenant()
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), TenantIdentity(), ValidatorServices(validator)),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
        validator.ObservedTenants.Should().Equal("tenant-1");
    }

    /// <summary>
    ///     Verifies that a tenant the registered validator rejects fails the tenant policy with the stable code.
    /// </summary>
    [Fact]
    public async Task Tenant_denies_a_malformed_tenant_with_the_stable_code()
    {
        var validator = new FixedTenantValidator(valid: false);

        var result = await MessagePolicy.Tenant()
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    TenantIdentityFor("not-a-workspace"),
                    ValidatorServices(validator)),
                CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(TenancyRefusalCodes.Invalid);
        deny.Reason.Should().Contain("not-a-workspace");
        TenancyRefusalCodes.Invalid.Should().Be("tenancy.invalid");
    }

    /// <summary>
    ///     Verifies that the tenant policy never consults the validator when the mediation runs in no tenant.
    /// </summary>
    [Fact]
    public async Task Tenant_skips_the_validator_without_a_tenant()
    {
        var validator = new FixedTenantValidator(valid: true);

        var result = await MessagePolicy.Tenant()
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), ValidatorServices(validator)),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().BeOfType<PolicyDeny>();
        validator.ObservedTenants.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that a held permission passes the permission policy.
    /// </summary>
    [Fact]
    public async Task Permission_allows_when_the_lookup_grants_it()
    {
        var options = new PolicyTestOptions();
        options.Granted.Add("vaults.open");

        var result = await MessagePolicy.Permission("vaults.open")
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), LookupServices(options)),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a missing permission fails with the stable code.
    /// </summary>
    [Fact]
    public async Task Permission_denies_with_the_stable_code_when_the_lookup_refuses()
    {
        var options = new PolicyTestOptions();
        options.Granted.Add("vaults.read");

        var result = await MessagePolicy.Permission("vaults.open")
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), LookupServices(options)),
                CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.MissingPermission);
        deny.Reason.Should().Contain("vaults.open");
        AuthorizationPolicyRefusalCodes.MissingPermission.Should().Be("authorization.missing-permission");
    }

    /// <summary>
    ///     Verifies that a permission policy without a registered lookup faults as unconfigured.
    /// </summary>
    [Fact]
    public async Task Permission_faults_with_the_stable_code_when_none_is_registered()
    {
        var result = await MessagePolicy.Permission("vaults.open")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.PermissionLookupMissing);
        fault.Reason.Should().Contain("vaults.open");
        AuthorizationPolicyRefusalCodes.PermissionLookupMissing.Should().Be("authorization.permission-lookup-missing");
    }

    /// <summary>
    ///     Verifies that a carried claim type passes the claim policy.
    /// </summary>
    [Fact]
    public async Task Claim_allows_when_the_principal_carries_the_type()
    {
        var identity = SignedInIdentity(new Claim("department", "vaults"));

        var result = await MessagePolicy.Claim("department")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a carried claim with the required value passes the claim policy.
    /// </summary>
    [Fact]
    public async Task Claim_allows_when_the_value_matches()
    {
        var identity = SignedInIdentity(new Claim("department", "vaults"));

        var result = await MessagePolicy.Claim("department", "vaults")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a missing claim fails with the stable code.
    /// </summary>
    [Fact]
    public async Task Claim_denies_when_the_principal_lacks_the_type()
    {
        var identity = SignedInIdentity(new Claim("department", "vaults"));

        var result = await MessagePolicy.Claim("clearance")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
        AuthorizationPolicyRefusalCodes.Forbidden.Should().Be("authorization.forbidden");
    }

    /// <summary>
    ///     Verifies that a claim with another value fails the valued claim policy.
    /// </summary>
    [Fact]
    public async Task Claim_denies_when_the_value_differs()
    {
        var identity = SignedInIdentity(new Claim("department", "vaults"));

        var result = await MessagePolicy.Claim("department", "ledger")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that an anonymous caller fails the claim policy as not signed in.
    /// </summary>
    [Fact]
    public async Task Claim_denies_an_anonymous_caller_as_not_signed_in()
    {
        var result = await MessagePolicy.Claim("department")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
    }

    /// <summary>
    ///     Verifies that a fresh sign-in passes the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_allows_a_fresh_sign_in()
    {
        var handledAt = TestInstant();
        var identity = SignedInIdentity(SignedInAt(handledAt.AddMinutes(-5)));

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a stale sign-in fails with the stable code.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_a_stale_sign_in()
    {
        var handledAt = TestInstant();
        var identity = SignedInIdentity(SignedInAt(handledAt.AddHours(-2)));

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
        deny.Reason.Should().Contain("00:30:00");
    }

    /// <summary>
    ///     Verifies that a principal without a sign-in instant faults the recent sign-in policy with the stable code.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_faults_for_a_principal_without_a_sign_in_instant()
    {
        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
        AuthorizationPolicyRefusalCodes.SignInInstantUnknown.Should().Be("authorization.sign-in-instant-unknown");
    }

    /// <summary>
    ///     Verifies that an anonymous caller fails the recent sign-in policy as not signed in.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_an_anonymous_caller_as_not_signed_in()
    {
        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
    }

    /// <summary>
    ///     Verifies that an unparsable sign-in instant faults the recent sign-in policy with the stable code.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_faults_for_an_unparsable_sign_in_instant()
    {
        var identity = SignedInIdentity(new Claim("auth_time", "not-a-number"));

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
    }

    /// <summary>
    ///     Verifies that a sign-in instant outside the representable range faults the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_faults_for_an_out_of_range_sign_in_instant()
    {
        var identity = SignedInIdentity(new Claim("auth_time", long.MaxValue.ToString(CultureInfo.InvariantCulture)));

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
    }

    /// <summary>
    ///     Verifies that a fresh sign-in read from the registered source passes the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_allows_a_fresh_sign_in_from_the_instant_source()
    {
        var handledAt = TestInstant();

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    SourceServices(new FixedSignInInstantSource(handledAt.AddMinutes(-5))),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a stale sign-in read from the registered source fails the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_a_stale_sign_in_from_the_instant_source()
    {
        var handledAt = TestInstant();

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    SourceServices(new FixedSignInInstantSource(handledAt.AddHours(-2))),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that an unknown instant from the registered source faults the recent sign-in policy.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_faults_when_the_source_returns_no_instant()
    {
        var handledAt = TestInstant();

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(SignedInAt(handledAt.AddMinutes(-5))),
                    SourceServices(new FixedSignInInstantSource(null)),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
    }

    /// <summary>
    ///     Verifies that the registered source wins over the claim when both carry an instant.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_prefers_the_source_over_the_claim()
    {
        var handledAt = TestInstant();
        var identity = SignedInIdentity(SignedInAt(handledAt.AddHours(-2)));

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    identity,
                    SourceServices(new FixedSignInInstantSource(handledAt.AddMinutes(-5))),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a sign-in older than a sub-second window fails, proving the age comparison keeps
    ///     sub-second precision.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_denies_a_sign_in_older_than_a_sub_second_window()
    {
        var handledAt = TestInstant();

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMilliseconds(500))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    SourceServices(new FixedSignInInstantSource(handledAt.AddMilliseconds(-600))),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that a sign-in within a sub-second window passes.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_allows_a_sign_in_within_a_sub_second_window()
    {
        var handledAt = TestInstant();

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMilliseconds(500))
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    SourceServices(new FixedSignInInstantSource(handledAt.AddMilliseconds(-400))),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that the claim overload reads the configured claim type.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_reads_the_configured_claim_type()
    {
        var handledAt = TestInstant();
        var identity = SignedInIdentity(CustomSignedInAt("session_auth_time", handledAt.AddMinutes(-5)));

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30), "session_auth_time")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that the claim overload ignores the default claim when another type is configured.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_faults_when_only_an_unconfigured_claim_carries_an_instant()
    {
        var handledAt = TestInstant();
        var identity = SignedInIdentity(SignedInAt(handledAt.AddMinutes(-5)));

        var result = await MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(30), "session_auth_time")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
    }

    /// <summary>
    ///     Verifies that a resolving window is read once per evaluation with the evaluation tenant.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_resolves_the_window_once_per_evaluation_with_the_evaluation_tenant()
    {
        var handledAt = TestInstant();
        var window = new CountingSignInWindow(TimeSpan.FromMinutes(30));
        var identity = TenantIdentityFor("tenant-1", SignedInAt(handledAt.AddMinutes(-5)));

        var result = await MessagePolicy.RecentSignIn(window)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
        window.CallCount.Should().Be(1);
        window.ObservedTenants.Should().Equal("tenant-1");
    }

    /// <summary>
    ///     Verifies that one resolving policy applies different windows to different tenants.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_applies_different_windows_to_different_tenants()
    {
        var handledAt = TestInstant();
        var window = new TenantSignInWindow(
            new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
            {
                ["strict-tenant"] = TimeSpan.FromMinutes(5),
                ["lenient-tenant"] = TimeSpan.FromMinutes(30)
            },
            TimeSpan.FromMinutes(15));
        var policy = MessagePolicy.RecentSignIn(window);

        var strict = await policy
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    TenantIdentityFor("strict-tenant", SignedInAt(handledAt.AddMinutes(-10))),
                    handledAt: handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        var lenient = await policy
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    TenantIdentityFor("lenient-tenant", SignedInAt(handledAt.AddMinutes(-10))),
                    handledAt: handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        strict.Should().BeOfType<PolicyDeny>();
        lenient.Should().Be(PolicyResult.Allow);
        window.CallCount.Should().Be(2);
    }

    /// <summary>
    ///     Verifies that a resolving policy reads a new window on each evaluation.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_applies_a_new_window_on_each_evaluation()
    {
        var handledAt = TestInstant();
        var window = new VaryingSignInWindow([TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)]);
        var policy = MessagePolicy.RecentSignIn(window);
        var identity = SignedInIdentity(SignedInAt(handledAt.AddMinutes(-10)));

        var first = await policy
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        var second = await policy
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity, handledAt: handledAt), CancellationToken.None)
            .ConfigureAwait(false);

        first.Should().BeOfType<PolicyDeny>();
        second.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that an unsigned caller fails before the resolving window is read.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_skips_the_window_for_an_unsigned_caller()
    {
        var window = new CountingSignInWindow(TimeSpan.FromMinutes(30));

        var result = await MessagePolicy.RecentSignIn(window)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
        window.CallCount.Should().Be(0);
    }

    /// <summary>
    ///     Verifies that the generic overload resolves the window from the dispatch scope on each evaluation.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_resolves_the_window_from_the_dispatch_scope()
    {
        var handledAt = TestInstant();
        var window = new CountingSignInWindow(TimeSpan.FromMinutes(30));

        var result = await MessagePolicy.RecentSignIn<CountingSignInWindow>()
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(SignedInAt(handledAt.AddMinutes(-5))),
                    WindowServices(window),
                    handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
        window.CallCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies that the generic overload faults when the window type is not registered.
    /// </summary>
    [Fact]
    public async Task RecentSignIn_faults_when_the_window_type_is_not_registered()
    {
        var handledAt = TestInstant();

        var result = await MessagePolicy.RecentSignIn<UnregisteredSignInWindow>()
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(SignedInAt(handledAt.AddMinutes(-5))),
                    handledAt: handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.RecentSignInWindowMissing);
        fault.Reason.Should().Contain(nameof(UnregisteredSignInWindow));
        AuthorizationPolicyRefusalCodes.RecentSignInWindowMissing.Should().Be("authorization.recent-sign-in-window-missing");
    }

    /// <summary>
    ///     Verifies that a run without a principal passes the system policy.
    /// </summary>
    [Fact]
    public async Task System_allows_a_run_without_a_principal()
    {
        var result = await MessagePolicy.System()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a call with a principal fails the system policy.
    /// </summary>
    [Fact]
    public async Task System_denies_a_call_with_a_principal()
    {
        var result = await MessagePolicy.System()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that the named subject passes the ownership policy.
    /// </summary>
    [Fact]
    public async Task Self_allows_the_subject_the_message_names()
    {
        var identity = SignedInIdentity(new Claim(ClaimTypes.NameIdentifier, "subject-1"));

        var result = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that the ownership policy reads the subject claim when no framework identifier is present.
    /// </summary>
    [Fact]
    public async Task Self_reads_the_subject_claim_without_a_framework_identifier()
    {
        var identity = SignedInIdentity(new Claim("sub", "subject-1"));

        var result = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that another subject fails the ownership policy.
    /// </summary>
    [Fact]
    public async Task Self_denies_a_caller_other_than_the_subject()
    {
        var identity = SignedInIdentity(new Claim(ClaimTypes.NameIdentifier, "subject-2"));

        var result = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that a message of another type fails the ownership policy rather than passing it.
    /// </summary>
    [Fact]
    public async Task Self_denies_a_message_of_another_type()
    {
        var result = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicyOtherMessage(), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that a message naming no subject fails the ownership policy.
    /// </summary>
    [Fact]
    public async Task Self_denies_a_message_naming_no_subject()
    {
        var identity = SignedInIdentity(new Claim(ClaimTypes.NameIdentifier, "subject-1"));

        var result = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage(null), identity), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that an anonymous caller fails the ownership policy as not signed in.
    /// </summary>
    [Fact]
    public async Task Self_denies_an_anonymous_caller_as_not_signed_in()
    {
        var result = await MessagePolicy.Self<PolicySubjectMessage>(message => message.SubjectId)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSignedIn);
    }

    /// <summary>
    ///     Verifies that a conjunction allows when every inner policy allows.
    /// </summary>
    [Fact]
    public async Task All_allows_when_every_inner_policy_allows()
    {
        var result = await MessagePolicy.All(MessagePolicy.SignedIn(), MessagePolicy.Allow())
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a conjunction returns the first deny.
    /// </summary>
    [Fact]
    public async Task All_returns_the_first_deny()
    {
        var result = await MessagePolicy.All(
                MessagePolicy.Allow(),
                MessagePolicy.Deny("the first refusal"),
                MessagePolicy.Deny("the second refusal"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Reason.Should().Be("the first refusal");
    }

    /// <summary>
    ///     Verifies that a conjunction stops at the first denial without evaluating what follows.
    /// </summary>
    [Fact]
    public async Task All_stops_at_the_first_denial()
    {
        var recorder = new PolicyEvaluationRecorder();

        var result = await MessagePolicy.All(
                new RecordingPolicy(recorder, "first", PolicyResult.Allow),
                new RecordingPolicy(recorder, "second", PolicyResult.Deny("test.first", "the first refusal")),
                new RecordingPolicy(recorder, "third", PolicyResult.Allow))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Reason.Should().Be("the first refusal");
        recorder.Steps.Should().Equal("first", "second");
    }

    /// <summary>
    ///     Verifies that a conjunction propagates a fault without letting a later allow erase it.
    /// </summary>
    [Fact]
    public async Task All_propagates_a_fault_past_a_later_allow()
    {
        var recorder = new PolicyEvaluationRecorder();

        var result = await MessagePolicy.All(
                new RecordingPolicy(recorder, "first", PolicyResult.Fault("test.unconfigured", "the port is missing.")),
                new RecordingPolicy(recorder, "second", PolicyResult.Allow))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be("test.unconfigured");
        recorder.Steps.Should().Equal("first");
    }

    /// <summary>
    ///     Verifies that a conjunction returns whichever of a denial and a fault comes first.
    /// </summary>
    [Fact]
    public async Task All_returns_whichever_of_a_denial_and_a_fault_comes_first()
    {
        var identity = SignedInIdentity();

        var denyFirst = await MessagePolicy.All(
                MessagePolicy.Deny("the refusal"),
                MessagePolicy.Permission("vaults.open"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        var faultFirst = await MessagePolicy.All(
                MessagePolicy.Permission("vaults.open"),
                MessagePolicy.Deny("the refusal"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        denyFirst.Should().BeOfType<PolicyDeny>();
        var fault = faultFirst.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.PermissionLookupMissing);
    }

    /// <summary>
    ///     Verifies that a disjunction allows when one inner policy allows.
    /// </summary>
    [Fact]
    public async Task Any_allows_when_one_inner_policy_allows()
    {
        var result = await MessagePolicy.Any(
                MessagePolicy.Deny("the first refusal"),
                MessagePolicy.Allow())
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a disjunction allows when a branch allows after a fault.
    /// </summary>
    [Fact]
    public async Task Any_allows_when_a_branch_allows_after_a_fault()
    {
        var result = await MessagePolicy.Any(
                MessagePolicy.Permission("vaults.open"),
                MessagePolicy.Allow())
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a disjunction returns the first deny when nothing allows.
    /// </summary>
    [Fact]
    public async Task Any_returns_the_first_deny_when_nothing_allows()
    {
        var result = await MessagePolicy.Any(
                MessagePolicy.Deny("the first refusal"),
                MessagePolicy.Deny("the second refusal"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Reason.Should().Be("the first refusal");
    }

    /// <summary>
    ///     Verifies that a disjunction returns the first denial when branches deny and fault.
    /// </summary>
    [Fact]
    public async Task Any_returns_the_first_denial_when_branches_deny_and_fault()
    {
        var result = await MessagePolicy.Any(
                MessagePolicy.Permission("vaults.open"),
                MessagePolicy.Deny("the first refusal"),
                MessagePolicy.Deny("the second refusal"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Reason.Should().Be("the first refusal");
    }

    /// <summary>
    ///     Verifies that a disjunction returns the fault when no branch was decidable.
    /// </summary>
    [Fact]
    public async Task Any_returns_the_fault_when_nothing_was_decidable()
    {
        var result = await MessagePolicy.Any(
                MessagePolicy.Permission("vaults.open"),
                MessagePolicy.Condition<FixedPolicyCondition>())
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.PermissionLookupMissing);
    }

    /// <summary>
    ///     Verifies that an empty disjunction denies.
    /// </summary>
    [Fact]
    public async Task Any_denies_an_empty_disjunction()
    {
        var result = await MessagePolicy.Any()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that a false condition skips the inner policy.
    /// </summary>
    [Fact]
    public async Task When_skips_the_inner_policy_when_the_condition_is_false()
    {
        var result = await MessagePolicy.When(_ => false, MessagePolicy.Deny("the inner refusal"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a true condition applies the inner policy.
    /// </summary>
    [Fact]
    public async Task When_applies_the_inner_policy_when_the_condition_holds()
    {
        var result = await MessagePolicy.When(_ => true, MessagePolicy.Deny("the inner refusal"))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Reason.Should().Be("the inner refusal");
    }

    /// <summary>
    ///     Verifies that the allowing policy allows.
    /// </summary>
    [Fact]
    public async Task Allow_always_allows()
    {
        var result = await MessagePolicy.Allow()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that the public policy allows without a caller.
    /// </summary>
    [Fact]
    public async Task Public_allows_without_a_caller()
    {
        var result = await MessagePolicy.Public()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), ExecutionIdentity.Anonymous), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that the public policy allows a signed-in caller.
    /// </summary>
    [Fact]
    public async Task Public_allows_a_signed_in_caller()
    {
        var result = await MessagePolicy.Public()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that the denying policy denies with its reason and the stable code.
    /// </summary>
    [Fact]
    public async Task Deny_always_denies_with_the_reason()
    {
        var result = await MessagePolicy.Deny("the message is closed")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Reason.Should().Be("the message is closed");
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
    }

    /// <summary>
    ///     Verifies that a registered condition answers the result the condition policy returns.
    /// </summary>
    [Fact]
    public async Task Condition_answers_the_registered_condition_result()
    {
        var result = await MessagePolicy.Condition<FixedPolicyCondition>()
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    ConditionServices(new FixedPolicyCondition(PolicyResult.Allow))),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a denying condition refuses through the condition policy with its own code.
    /// </summary>
    [Fact]
    public async Task Condition_returns_the_condition_denial()
    {
        var result = await MessagePolicy.Condition<FixedPolicyCondition>()
            .EvaluateAsync(
                ContextFor(
                    new PolicySubjectMessage("subject-1"),
                    SignedInIdentity(),
                    ConditionServices(new FixedPolicyCondition(
                        PolicyResult.Deny("test.condition", "the condition refused.")))),
                CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Reason.Should().Be("the condition refused.");
        deny.Code.Should().Be("test.condition");
    }

    /// <summary>
    ///     Verifies that a condition policy without a registered condition faults with the stable code.
    /// </summary>
    [Fact]
    public async Task Condition_faults_with_the_stable_code_when_none_is_registered()
    {
        var result = await MessagePolicy.Condition<FixedPolicyCondition>()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.ConditionMissing);
        fault.Reason.Should().Contain(nameof(FixedPolicyCondition));
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

        var result = await MessagePolicy.SentBy<PolicySubjectMessage>()
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), executionContext: current),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
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

        var result = await MessagePolicy.SentBy<object>()
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), executionContext: current),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that a mediation with no execution context fails the sent-by policy with the stable code.
    /// </summary>
    [Fact]
    public async Task SentBy_denies_without_an_execution_context()
    {
        var result = await MessagePolicy.SentBy<PolicySubjectMessage>()
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSentBy);
        AuthorizationPolicyRefusalCodes.NotSentBy.Should().Be("authorization.not-sent-by");
    }

    /// <summary>
    ///     Verifies that a root mediation fails the sent-by policy.
    /// </summary>
    [Fact]
    public async Task SentBy_denies_a_root_mediation()
    {
        var current = new PolicyExecutionContext(messageType: typeof(PolicySubjectMessage));

        var result = await MessagePolicy.SentBy<PolicySubjectMessage>()
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), executionContext: current),
                CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSentBy);
    }

    /// <summary>
    ///     Verifies that a nested mediation whose parent handles another message fails the sent-by policy.
    /// </summary>
    [Fact]
    public async Task SentBy_denies_when_the_parent_handles_another_message()
    {
        var parent = new PolicyExecutionContext(messageType: typeof(PolicyOtherMessage));
        var current = new PolicyExecutionContext(parent: parent);

        var result = await MessagePolicy.SentBy<PolicySubjectMessage>()
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), executionContext: current),
                CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be(AuthorizationPolicyRefusalCodes.NotSentBy);
        deny.Reason.Should().Contain(nameof(PolicySubjectMessage));
    }

    /// <summary>
    ///     Verifies that a decision policy answers what its decision point decides.
    /// </summary>
    [Fact]
    public async Task Decide_answers_what_the_decision_point_decides()
    {
        var decisionPoint = new RecordingDecisionPoint(PolicyResult.Deny("ledger.closed", "the ledger is closed."));

        var result = await MessagePolicy.Decide<RecordingDecisionPoint>("ledger.close")
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), DecisionServices(decisionPoint)),
                CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be("ledger.closed");
        deny.Reason.Should().Be("the ledger is closed.");
    }

    /// <summary>
    ///     Verifies that a decision policy passes a decision-point fault through unchanged.
    /// </summary>
    [Fact]
    public async Task Decide_passes_a_decision_point_fault_through()
    {
        var decisionPoint = new RecordingDecisionPoint(
            PolicyResult.Fault("ledger.unreachable", "the decision service is unreachable."));

        var result = await MessagePolicy.Decide<RecordingDecisionPoint>("ledger.close")
            .EvaluateAsync(
                ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity(), DecisionServices(decisionPoint)),
                CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be("ledger.unreachable");
    }

    /// <summary>
    ///     Verifies that the decision request carries the subject, action, message, resource, and instant.
    /// </summary>
    [Fact]
    public async Task Decide_sends_the_subject_action_message_and_resource_to_the_decision_point()
    {
        var handledAt = TestInstant();
        var message = new PolicySubjectMessage("subject-1");
        var identity = TenantIdentityFor("tenant-1");
        var decisionPoint = new RecordingDecisionPoint(PolicyResult.Allow);

        var result = await MessagePolicy.Decide<RecordingDecisionPoint>("ledger.close", "ledger", "ledger-7")
            .EvaluateAsync(
                ContextFor(message, identity, DecisionServices(decisionPoint), handledAt),
                CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);

        var request = decisionPoint.Requests.Should().ContainSingle().Which;
        request.Identity.Should().Be(identity);
        request.Action.Should().Be("ledger.close");
        request.Message.Should().BeSameAs(message);
        request.MessageType.Should().Be<PolicySubjectMessage>();
        request.ResourceKind.Should().Be("ledger");
        request.ResourceId.Should().Be("ledger-7");
        request.HandledAt.Should().Be(handledAt);
    }

    /// <summary>
    ///     Verifies that a decision policy without a registered decision point faults with the stable code.
    /// </summary>
    [Fact]
    public async Task Decide_faults_when_the_decision_point_is_not_registered()
    {
        var result = await MessagePolicy.Decide<UnregisteredDecisionPoint>("ledger.close")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.DecisionPointMissing);
        fault.Reason.Should().Contain(nameof(UnregisteredDecisionPoint));
        AuthorizationPolicyRefusalCodes.DecisionPointMissing.Should().Be("authorization.decision-point-missing");
    }

    /// <summary>
    ///     Verifies that the refusal wrapper replaces the code and reason of a denial.
    /// </summary>
    [Fact]
    public async Task WithRefusal_replaces_the_code_and_reason_of_a_denial()
    {
        var result = await MessagePolicy.Deny("the inner refusal").WithRefusal("product.closed", "the product refusal")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be("product.closed");
        deny.Reason.Should().Be("the product refusal");
    }

    /// <summary>
    ///     Verifies that the refusal wrapper keeps the inner reason when none is given.
    /// </summary>
    [Fact]
    public async Task WithRefusal_keeps_the_inner_reason_when_none_is_given()
    {
        var result = await MessagePolicy.Deny("the inner refusal").WithRefusal("product.closed")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be("product.closed");
        deny.Reason.Should().Be("the inner refusal");
    }

    /// <summary>
    ///     Verifies that the refusal wrapper passes an allow through unchanged.
    /// </summary>
    [Fact]
    public async Task WithRefusal_passes_an_allow_through()
    {
        var result = await MessagePolicy.Allow().WithRefusal("product.closed", "the product refusal")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        result.Should().Be(PolicyResult.Allow);
    }

    /// <summary>
    ///     Verifies that the refusal wrapper passes a fault through unchanged.
    /// </summary>
    [Fact]
    public async Task WithRefusal_passes_a_fault_through()
    {
        var result = await MessagePolicy.Permission("vaults.open").WithRefusal("product.closed", "the product refusal")
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.PermissionLookupMissing);
        fault.Reason.Should().Contain("vaults.open");
    }

    /// <summary>
    ///     Verifies that the filtered wrapper rewrites only the denial carrying the named inner code.
    /// </summary>
    [Fact]
    public async Task WithRefusal_replaces_only_the_named_inner_denial()
    {
        var identity = SignedInIdentity();

        var rewritten = await MessagePolicy.Deny("the inner refusal")
            .WithRefusal("product.closed", "the product refusal", AuthorizationPolicyRefusalCodes.Forbidden)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        var kept = await MessagePolicy.Deny("the inner refusal")
            .WithRefusal("product.closed", "the product refusal", AuthorizationPolicyRefusalCodes.MissingPermission)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), identity), CancellationToken.None)
            .ConfigureAwait(false);

        var rewrittenDeny = rewritten.Should().BeOfType<PolicyDeny>().Subject;
        rewrittenDeny.Code.Should().Be("product.closed");
        rewrittenDeny.Reason.Should().Be("the product refusal");

        var keptDeny = kept.Should().BeOfType<PolicyDeny>().Subject;
        keptDeny.Code.Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
        keptDeny.Reason.Should().Be("the inner refusal");
    }

    /// <summary>
    ///     Verifies that the filtered wrapper passes a fault through unchanged.
    /// </summary>
    [Fact]
    public async Task WithRefusal_with_a_filter_passes_a_fault_through()
    {
        var result = await MessagePolicy.Permission("vaults.open")
            .WithRefusal("product.closed", "the product refusal", AuthorizationPolicyRefusalCodes.PermissionLookupMissing)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var fault = result.Should().BeOfType<PolicyFault>().Subject;
        fault.Code.Should().Be(AuthorizationPolicyRefusalCodes.PermissionLookupMissing);
    }

    /// <summary>
    ///     Verifies that the filtered wrapper keeps the inner reason when none is given.
    /// </summary>
    [Fact]
    public async Task WithRefusal_with_a_filter_keeps_the_inner_reason_when_none_is_given()
    {
        var result = await MessagePolicy.Deny("the inner refusal")
            .WithRefusal("product.closed", null, AuthorizationPolicyRefusalCodes.Forbidden)
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be("product.closed");
        deny.Reason.Should().Be("the inner refusal");
    }

    /// <summary>
    ///     Verifies that two policies built the same way are equal.
    /// </summary>
    [Fact]
    public void Identical_policies_are_equal()
    {
        MessagePolicy.SignedIn().Should().Be(MessagePolicy.SignedIn());
        MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(5)).Should().Be(MessagePolicy.RecentSignIn(TimeSpan.FromMinutes(5)));
        MessagePolicy.SignedIn().Should().NotBe(MessagePolicy.Allow());
    }

    /// <summary>
    ///     Verifies that a record subclass with an explicit constructor evaluates.
    /// </summary>
    [Fact]
    public async Task A_record_subclass_with_an_explicit_constructor_evaluates()
    {
        var result = await new ExplicitConstructorPolicy(PolicyResult.Deny("test.custom", "the custom refusal."))
            .EvaluateAsync(ContextFor(new PolicySubjectMessage("subject-1"), SignedInIdentity()), CancellationToken.None)
            .ConfigureAwait(false);

        var deny = result.Should().BeOfType<PolicyDeny>().Subject;
        deny.Code.Should().Be("test.custom");
    }

    /// <summary>
    ///     Verifies that the refusal and fault codes carry the documented values.
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
        AuthorizationPolicyRefusalCodes.DecisionPointMissing.Should().Be("authorization.decision-point-missing");
        AuthorizationPolicyRefusalCodes.SignInInstantUnknown.Should().Be("authorization.sign-in-instant-unknown");
        AuthorizationPolicyRefusalCodes.RecentSignInWindowMissing.Should().Be("authorization.recent-sign-in-window-missing");
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
    ///     Verifies that a null sign-in window is rejected.
    /// </summary>
    [Fact]
    public void A_null_sign_in_window_is_rejected()
    {
        var act = () => MessagePolicy.RecentSignIn(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies that a blank decision action is rejected where it is written.
    /// </summary>
    /// <param name="action">The blank action under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_decision_action_is_rejected_where_it_is_written(string? action)
    {
        var act = () => MessagePolicy.Decide<RecordingDecisionPoint>(action!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that a blank decision resource kind is rejected where it is written.
    /// </summary>
    /// <param name="resourceKind">The blank resource kind under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_decision_resource_kind_is_rejected_where_it_is_written(string? resourceKind)
    {
        var act = () => MessagePolicy.Decide<RecordingDecisionPoint>("ledger.close", resourceKind);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies that a blank decision resource identifier is rejected where it is written.
    /// </summary>
    /// <param name="resourceId">The blank resource identifier under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_decision_resource_id_is_rejected_where_it_is_written(string? resourceId)
    {
        var act = () => MessagePolicy.Decide<RecordingDecisionPoint>("ledger.close", "ledger", resourceId);

        act.Should().Throw<ArgumentException>();
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
    ///     Verifies that a blank inner code is rejected where the filtered wrapper is written.
    /// </summary>
    /// <param name="whenInnerCode">The blank inner code under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_inner_code_is_rejected_where_it_is_written(string? whenInnerCode)
    {
        var act = () => MessagePolicy.Deny("the inner refusal").WithRefusal("product.closed", null, whenInnerCode!);

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

    private static ExecutionIdentity TenantIdentityFor(string tenantId, params Claim[] claims)
    {
        return ExecutionIdentity.For(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")), tenantId);
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

    private static IServiceProvider WindowServices(IRecentSignInWindow window)
    {
        return new ServiceCollection()
            .AddSingleton(window.GetType(), window)
            .BuildServiceProvider();
    }

    private static IServiceProvider DecisionServices(IAuthorizationDecisionPoint decisionPoint)
    {
        return new ServiceCollection()
            .AddSingleton(decisionPoint.GetType(), decisionPoint)
            .BuildServiceProvider();
    }

    private static IServiceProvider ValidatorServices(ITenantIdentifierValidator validator)
    {
        return new ServiceCollection()
            .AddSingleton(validator)
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Answers a fixed policy result, so the condition policy tests control what the named condition decides.
    /// </summary>
    private sealed class FixedPolicyCondition : IAuthorizationCondition
    {
        /// <summary>
        ///     The result this condition answers.
        /// </summary>
        private readonly PolicyResult _result;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedPolicyCondition" /> class.
        /// </summary>
        /// <param name="result">The result this condition answers.</param>
        public FixedPolicyCondition(PolicyResult result)
        {
            _result = result;
        }

        /// <inheritdoc />
        public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<PolicyResult>(_result);
        }
    }

    /// <summary>
    ///     Answers configured windows in order, so window-per-call tests control what each evaluation reads.
    /// </summary>
    private sealed class VaryingSignInWindow : IRecentSignInWindow
    {
        /// <summary>
        ///     The windows this source answers, in evaluation order.
        /// </summary>
        private readonly Queue<TimeSpan> _windows;

        /// <summary>
        ///     Initializes a new instance of the <see cref="VaryingSignInWindow" /> class.
        /// </summary>
        /// <param name="windows">The windows this source answers, in evaluation order.</param>
        public VaryingSignInWindow(IEnumerable<TimeSpan> windows)
        {
            _windows = new Queue<TimeSpan>(windows);
        }

        /// <inheritdoc />
        public ValueTask<TimeSpan> GetWindowAsync(AuthorizationContext context, CancellationToken cancellationToken)
        {
            return new ValueTask<TimeSpan>(_windows.Dequeue());
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
    ///     A policy subclass with an explicit constructor, proving the documented subclass shape evaluates.
    /// </summary>
    private sealed record ExplicitConstructorPolicy : MessagePolicy
    {
        /// <summary>
        ///     The result this policy answers.
        /// </summary>
        private readonly PolicyResult _result;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ExplicitConstructorPolicy" /> class.
        /// </summary>
        /// <param name="result">The result this policy answers.</param>
        public ExplicitConstructorPolicy(PolicyResult result)
        {
            _result = result;
        }

        /// <inheritdoc />
        public override ValueTask<PolicyResult> EvaluateAsync(
            AuthorizationContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<PolicyResult>(_result);
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
