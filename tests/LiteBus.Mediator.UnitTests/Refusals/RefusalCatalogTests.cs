using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Mediator.UnitTests.Refusals;

/// <summary>
///     Verifies that the transport-neutral refusal catalog resolves codes, enforces the one conflict rule, and
///     reports its contents to the composition model.
/// </summary>
public sealed class RefusalCatalogTests
{
    [Theory]
    [InlineData("declaration.missing", "Request denied")]
    [InlineData("authorization.not-signed-in", "Not signed in")]
    [InlineData("authorization.forbidden", "Request denied")]
    [InlineData("authorization.missing-permission", "Request denied")]
    [InlineData("tenancy.required", "Request denied")]
    [InlineData("tenancy.invalid", "Invalid tenant")]
    [InlineData("validation.failed", "Request validation failed")]
    [InlineData("validation.amount.negative", "Request validation failed")]
    [InlineData("concurrency.conflict", "Concurrency conflict")]
    [InlineData("idempotency.in-flight", "Idempotent request in flight")]
    [InlineData("idempotency.fingerprint-mismatch", "Idempotency key already used")]
    [InlineData("litebus.batch.duplicate_idempotency_key", "Duplicate idempotency key in batch")]
    public void TryResolve_KnownCode_MatchesEntry(string code, string title)
    {
        var catalog = RefusalCatalog.CreateDefault();

        var matched = catalog.TryResolve(code, out var entry);

        matched.Should().BeTrue();
        entry!.Pattern.Should().Be(PatternFor(code));
        entry.Title.Should().Be(title);
        entry.Owner.Should().Be("LiteBus");
    }

    [Theory]
    [InlineData("declaration.missing", "Denied", false)]
    [InlineData("authorization.forbidden", "Denied", false)]
    [InlineData("authorization.not-signed-in", "Denied", false)]
    [InlineData("tenancy.required", "Denied", false)]
    [InlineData("tenancy.invalid", "Invalid", false)]
    [InlineData("validation.failed", "Invalid", false)]
    [InlineData("concurrency.conflict", "Conflict", true)]
    [InlineData("idempotency.in-flight", "Conflict", true)]
    [InlineData("idempotency.fingerprint-mismatch", "Invalid", false)]
    [InlineData("litebus.batch.duplicate_idempotency_key", "Conflict", false)]
    public void TryResolve_KnownCode_ReportsKindAndRetryability(string code, string kind, bool retryable)
    {
        var catalog = RefusalCatalog.CreateDefault();

        catalog.TryResolve(code, out var entry).Should().BeTrue();

        KindName(entry!.Kind).Should().Be(kind);
        entry.Retryable.Should().Be(retryable);
    }

    [Theory]
    [InlineData("billing.unpaid")]
    [InlineData("authorization")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_UnknownCode_ReportsNoMatch(string? code)
    {
        var catalog = RefusalCatalog.CreateDefault();

        var matched = catalog.TryResolve(code, out var entry);

        matched.Should().BeFalse();
        entry.Should().BeNull();
    }

    [Fact]
    public void TryResolve_ExactMapping_WinsOverPrefixPattern()
    {
        var catalog = RefusalCatalog.CreateDefault();
        catalog.Map("authorization.special", RefusalKind.Conflict, "Special conflict", retryable: true);

        catalog.TryResolve("authorization.special", out var entry).Should().BeTrue();

        entry!.Kind.Should().Be(RefusalKind.Conflict);
        entry.Title.Should().Be("Special conflict");
    }

    [Fact]
    public void TryResolve_LongestPrefix_WinsWhenSeveralPatternsCoverOneCode()
    {
        var catalog = new RefusalCatalog();
        catalog.Map("billing.*", RefusalKind.Denied, "Billing denied", retryable: false);
        catalog.Map("billing.tax.*", RefusalKind.Invalid, "Tax validation failed", retryable: false);

        catalog.TryResolve("billing.tax.missing", out var entry).Should().BeTrue();

        entry!.Pattern.Should().Be("billing.tax.*");
    }

    [Fact]
    public void TryResolve_ExactCode_DoesNotMatchLongerCodes()
    {
        var catalog = new RefusalCatalog();
        catalog.Map("billing.unpaid", RefusalKind.Denied, "Billing denied", retryable: false);

        catalog.TryResolve("billing.unpaid.line", out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_PrefixPattern_DoesNotMatchTheBarePrefix()
    {
        var catalog = new RefusalCatalog();
        catalog.Map("billing.*", RefusalKind.Denied, "Billing denied", retryable: false);

        catalog.TryResolve("billing", out _).Should().BeFalse();
    }

    [Fact]
    public void Map_IdenticalRepeat_IsNoOpKeepingFirstOwner()
    {
        var catalog = new RefusalCatalog();
        catalog.Map("billing.unpaid", RefusalKind.Conflict, "Billing conflict", retryable: true, owner: "Billing");
        catalog.Map("billing.unpaid", RefusalKind.Conflict, "Billing conflict", retryable: true, owner: "Checkout");

        catalog.TryResolve("billing.unpaid", out var entry).Should().BeTrue();
        entry!.Owner.Should().Be("Billing");
        catalog.Entries.Should().ContainSingle();
    }

    [Fact]
    public void Map_ConflictingRemap_ThrowsNamingCodeAndBothMappings()
    {
        var catalog = RefusalCatalog.CreateDefault();

        var exception = Assert.Throws<LiteBusConfigurationException>(
            () => catalog.Map(
                TenancyRefusalCodes.TenantRequired,
                RefusalKind.Invalid,
                "Tenant sign-in required",
                retryable: false,
                owner: "Tenancy"));

        exception.Message.Should().Contain(TenancyRefusalCodes.TenantRequired);
        exception.Message.Should().Contain("Denied");
        exception.Message.Should().Contain("Request denied");
        exception.Message.Should().Contain("LiteBus");
        exception.Message.Should().Contain("Invalid");
        exception.Message.Should().Contain("Tenant sign-in required");
        exception.Message.Should().Contain("Tenancy");
        exception.Message.Should().Contain("Replace");
    }

    [Fact]
    public void Replace_ExistingMapping_OverridesMapping()
    {
        var catalog = RefusalCatalog.CreateDefault();
        catalog.Replace(
            TenancyRefusalCodes.TenantRequired,
            RefusalKind.Invalid,
            "Tenant sign-in required",
            retryable: false,
            owner: "Tenancy");

        catalog.TryResolve(TenancyRefusalCodes.TenantRequired, out var entry).Should().BeTrue();

        entry!.Kind.Should().Be(RefusalKind.Invalid);
        entry.Title.Should().Be("Tenant sign-in required");
        entry.Owner.Should().Be("Tenancy");
    }

    [Fact]
    public void Replace_NewPattern_AddsMapping()
    {
        var catalog = new RefusalCatalog();
        catalog.Replace("billing.unpaid", RefusalKind.Conflict, "Billing conflict", retryable: true, owner: "Billing");

        catalog.TryResolve("billing.unpaid", out var entry).Should().BeTrue();

        entry!.Title.Should().Be("Billing conflict");
    }

    [Fact]
    public void Entries_ListsEveryMappingWithItsOwnerOrderedByPattern()
    {
        var catalog = RefusalCatalog.CreateDefault();
        catalog.Map("billing.unpaid", RefusalKind.Conflict, "Billing conflict", retryable: true, owner: "Billing");

        var entries = catalog.Entries;

        entries.Should().HaveCount(11);
        var patterns = entries.Select(entry => entry.Pattern).ToList();
        patterns.Should().Equal(patterns.OrderBy(pattern => pattern, StringComparer.Ordinal));
        var billing = entries.Single(entry => entry.Pattern == "billing.unpaid");
        billing.Kind.Should().Be(RefusalKind.Conflict);
        billing.Owner.Should().Be("Billing");
        entries.Where(entry => entry.Pattern != "billing.unpaid").Should().OnlyContain(entry => entry.Owner == "LiteBus");
    }

    [Fact]
    public void CreateDefault_RegistersNoFaultCode()
    {
        var catalog = RefusalCatalog.CreateDefault();

        var patterns = catalog.Entries.Select(entry => entry.Pattern).ToList();

        patterns.Should().NotContain(AuthorizationPolicyRefusalCodes.UnknownPolicy);
        patterns.Should().NotContain(AuthorizationPolicyRefusalCodes.PermissionLookupMissing);
        patterns.Should().NotContain(AuthorizationPolicyRefusalCodes.ConditionMissing);
        patterns.Should().NotContain(AuthorizationPolicyRefusalCodes.DecisionPointMissing);
        patterns.Should().NotContain(AuthorizationPolicyRefusalCodes.SignInInstantUnknown);
        patterns.Should().NotContain(AuthorizationPolicyRefusalCodes.RecentSignInWindowMissing);
    }

    [Fact]
    public void CompositionSection_NamesRefusalsAndListsEntries()
    {
        var catalog = RefusalCatalog.CreateDefault();
        catalog.Map("billing.unpaid", RefusalKind.Conflict, "Billing conflict", retryable: true, owner: "Billing");

        catalog.SectionName.Should().Be("refusals");

        var data = catalog.GetData().Should().BeOfType<SortedDictionary<string, object?>>().Subject;
        var entries = data["entries"].Should().BeAssignableTo<IReadOnlyList<SortedDictionary<string, object?>>>().Subject;

        entries.Should().HaveCount(11);
        var billing = entries.Single(entry => (string)entry["pattern"]! == "billing.unpaid");
        billing["kind"].Should().Be("Conflict");
        billing["title"].Should().Be("Billing conflict");
        billing["retryable"].Should().Be(true);
        billing["owner"].Should().Be("Billing");
    }

    [Fact]
    public void CompositionSection_EmptyCatalog_ContributesNothing()
    {
        var catalog = new RefusalCatalog();

        catalog.GetData().Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Map_BlankCode_Throws(string? code)
    {
        var catalog = new RefusalCatalog();

        Assert.ThrowsAny<ArgumentException>(
            () => catalog.Map(code!, RefusalKind.Denied, "Title", retryable: false));
    }

    [Fact]
    public void Map_NullKind_Throws()
    {
        var catalog = new RefusalCatalog();

        Assert.Throws<ArgumentNullException>(
            () => catalog.Map("billing.unpaid", null!, "Title", retryable: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Map_BlankTitle_Throws(string? title)
    {
        var catalog = new RefusalCatalog();

        Assert.ThrowsAny<ArgumentException>(
            () => catalog.Map("billing.unpaid", RefusalKind.Denied, title!, retryable: false));
    }

    [Theory]
    [InlineData("billing*")]
    [InlineData("*")]
    [InlineData(".*")]
    [InlineData("billing.*.unpaid")]
    public void Map_MalformedPattern_Throws(string code)
    {
        var catalog = new RefusalCatalog();

        Assert.Throws<ArgumentException>(
            () => catalog.Map(code, RefusalKind.Denied, "Title", retryable: false));
    }

    /// <summary>
    ///     Names the pattern a known code resolves through.
    /// </summary>
    /// <param name="code">The refusal code.</param>
    /// <returns>The exact pattern, or the prefix pattern covering the code.</returns>
    private static string PatternFor(string code)
    {
        if (code.StartsWith("authorization.", StringComparison.Ordinal)
            && code != AuthorizationPolicyRefusalCodes.NotSignedIn)
        {
            return "authorization.*";
        }

        if (code.StartsWith("validation.", StringComparison.Ordinal))
        {
            return "validation.*";
        }

        return code;
    }

    /// <summary>
    ///     Names a refusal kind the way the composition export names it.
    /// </summary>
    /// <param name="kind">The refusal kind.</param>
    /// <returns>The kind name.</returns>
    private static string KindName(RefusalKind kind)
    {
        return kind switch
        {
            RefusalKind.DeniedKind => nameof(RefusalKind.Denied),
            RefusalKind.InvalidKind => nameof(RefusalKind.Invalid),
            RefusalKind.ConflictKind => nameof(RefusalKind.Conflict),
            RefusalKind.NotFoundKind => nameof(RefusalKind.NotFound),
            RefusalKind.UnavailableKind => nameof(RefusalKind.Unavailable),
            _ => kind.GetType().Name,
        };
    }
}
