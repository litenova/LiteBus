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
    public void TryResolve_DefaultInterfaceMember_NonNullCodeReturnsTrueEvenForFallback()
    {
        IRefusalProblemCatalog catalog = new ResolveOnlyCatalog();

        var found = catalog.TryResolve("billing.unpaid", out var map);

        Assert.True(found);
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

    private sealed class ResolveOnlyCatalog : IRefusalProblemCatalog
    {
        public IRefusalProblemCatalog Map(string code, int statusCode, string title)
        {
            return this;
        }

        public RefusalProblemMap Resolve(string? code)
        {
            return new RefusalProblemMap(400, "Invalid request");
        }
    }
}
