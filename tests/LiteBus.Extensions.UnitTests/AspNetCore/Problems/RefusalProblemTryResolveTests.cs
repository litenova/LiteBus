using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Problems;

public sealed class RefusalProblemTryResolveTests
{
    [Fact]
    public void TryResolve_ExactHit_ReturnsTrueWithMapping()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();

        var found = catalog.TryResolve("authorization.not-signed-in", out var map);

        Assert.True(found);
        Assert.Equal(401, map.StatusCode);
        Assert.Equal("Not signed in", map.Title);
    }

    [Fact]
    public void TryResolve_PrefixHit_ReturnsTrueWithMapping()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();

        var found = catalog.TryResolve("authorization.forbidden", out var map);

        Assert.True(found);
        Assert.Equal(403, map.StatusCode);
        Assert.Equal("Request denied", map.Title);
    }

    [Fact]
    public void TryResolve_LongestPrefix_ReturnsTrueWithLongestMapping()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Map("billing.*", 402, "Billing required");
        catalog.Map("billing.tax.*", 422, "Tax validation failed");

        var found = catalog.TryResolve("billing.tax.missing", out var map);

        Assert.True(found);
        Assert.Equal(422, map.StatusCode);
        Assert.Equal("Tax validation failed", map.Title);
    }

    [Fact]
    public void TryResolve_CustomMapping_ReturnsTrue()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required");

        var found = catalog.TryResolve("billing.unpaid", out var map);

        Assert.True(found);
        Assert.Equal(402, map.StatusCode);
        Assert.Equal("Billing required", map.Title);
    }

    [Theory]
    [InlineData("billing.unpaid")]
    [InlineData("authorization")]
    [InlineData("billing")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_FallbackPath_ReturnsFalseWithFallbackMapping(string? code)
    {
        var catalog = RefusalProblemCatalog.CreateDefault();

        var found = catalog.TryResolve(code, out var map);

        Assert.False(found);
        Assert.Equal(400, map.StatusCode);
        Assert.Equal("Invalid request", map.Title);
    }

    [Fact]
    public void TryResolve_ExactCode_DoesNotMatchLongerCodes()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Map("billing.unpaid", 402, "Billing required");

        var found = catalog.TryResolve("billing.unpaid.line", out var map);

        Assert.False(found);
        Assert.Equal(400, map.StatusCode);
    }

    [Fact]
    public void TryResolve_PrefixPattern_DoesNotMatchTheBarePrefix()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Map("billing.*", 402, "Billing required");

        var found = catalog.TryResolve("billing", out var map);

        Assert.False(found);
        Assert.Equal(400, map.StatusCode);
    }

    [Fact]
    public void TryResolve_WithOutcome_ExactHit_ReturnsTrueWithMapping()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();

        var found = catalog.TryResolve("authorization.forbidden", MediationOutcome.Denied, out var map);

        Assert.True(found);
        Assert.Equal(403, map.StatusCode);
        Assert.Equal("Request denied", map.Title);
    }

    [Fact]
    public void TryResolve_WithOutcome_PrefixHit_ReturnsTrueWithMapping()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();

        var found = catalog.TryResolve("validation.amount.negative", MediationOutcome.Invalid, out var map);

        Assert.True(found);
        Assert.Equal(422, map.StatusCode);
        Assert.Equal("Request validation failed", map.Title);
    }

    [Fact]
    public void TryResolve_WithOutcome_UncodedInvalid_ReturnsFalseWithInvalidDefault()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();

        var found = catalog.TryResolve(null, MediationOutcome.Invalid, out var map);

        Assert.False(found);
        Assert.Equal(400, map.StatusCode);
        Assert.Equal("Invalid request", map.Title);
    }

    [Fact]
    public void TryResolve_WithOutcome_UnmappedDenied_ReturnsFalseWithDeniedDefault()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.MapDenied(403, "Request denied");

        var found = catalog.TryResolve("billing.unpaid", MediationOutcome.Denied, out var map);

        Assert.False(found);
        Assert.Equal(403, map.StatusCode);
        Assert.Equal("Request denied", map.Title);
    }

    [Fact]
    public void TryResolve_WithOutcome_UnmappedInvalid_ReturnsFalseWithInvalidDefault()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.MapInvalid(422, "Semantic errors");

        var found = catalog.TryResolve("billing.unpaid", MediationOutcome.Invalid, out var map);

        Assert.False(found);
        Assert.Equal(422, map.StatusCode);
        Assert.Equal("Semantic errors", map.Title);
    }

    [Fact]
    public void TryResolve_WithOutcome_ExactMapping_OutranksOutcomeDefault()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required");
        catalog.MapDenied(403, "Request denied");

        var found = catalog.TryResolve("billing.unpaid", MediationOutcome.Denied, out var map);

        Assert.True(found);
        Assert.Equal(402, map.StatusCode);
        Assert.Equal("Billing required", map.Title);
    }

    [Fact]
    public void Resolve_WithOutcome_AppliesOutcomeDefault()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.MapInvalid(422, "Semantic errors");

        var mapping = catalog.Resolve("billing.unpaid", MediationOutcome.Invalid);

        Assert.Equal(422, mapping.StatusCode);
        Assert.Equal("Semantic errors", mapping.Title);
    }

    [Fact]
    public void TryResolve_DefaultInterfaceMember_NonNullCodeReturnsFalse()
    {
        IRefusalProblemCatalog catalog = new ResolveOnlyCatalog();

        var found = catalog.TryResolve("billing.unpaid", out var map);

        Assert.False(found);
        Assert.Equal(400, map.StatusCode);
        Assert.Equal("Invalid request", map.Title);
    }

    [Fact]
    public void TryResolve_DefaultInterfaceMember_NullCodeReturnsFalse()
    {
        IRefusalProblemCatalog catalog = new ResolveOnlyCatalog();

        var found = catalog.TryResolve(null, out var map);

        Assert.False(found);
        Assert.Equal(400, map.StatusCode);
    }

    [Fact]
    public void TryResolve_WithOutcome_DefaultInterfaceMember_ReturnsFalse()
    {
        IRefusalProblemCatalog catalog = new ResolveOnlyCatalog();

        var found = catalog.TryResolve("billing.unpaid", MediationOutcome.Denied, out var map);

        Assert.False(found);
        Assert.Equal(400, map.StatusCode);
        Assert.Equal("Invalid request", map.Title);
    }

    [Fact]
    public void Resolve_WithOutcome_DefaultInterfaceMember_DelegatesToResolve()
    {
        IRefusalProblemCatalog catalog = new ResolveOnlyCatalog();

        var mapping = catalog.Resolve("billing.unpaid", MediationOutcome.Denied);

        Assert.Equal(400, mapping.StatusCode);
        Assert.Equal("Invalid request", mapping.Title);
    }

    private sealed class ResolveOnlyCatalog : IRefusalProblemCatalog
    {
        public IRefusalProblemCatalog Map(string code, int statusCode, string title, string? contributor = null)
        {
            return this;
        }

        public IRefusalProblemCatalog Replace(string code, int statusCode, string title, string? contributor = null)
        {
            return this;
        }

        public IRefusalProblemCatalog MapInvalid(int statusCode, string title)
        {
            return this;
        }

        public IRefusalProblemCatalog MapDenied(int statusCode, string title)
        {
            return this;
        }

        public RefusalProblemMap Resolve(string? code)
        {
            return new RefusalProblemMap(400, "Invalid request");
        }
    }
}
