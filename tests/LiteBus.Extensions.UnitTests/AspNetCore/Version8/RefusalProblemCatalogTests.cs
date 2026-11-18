using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Version8;

public sealed class RefusalProblemCatalogTests
{
    [Theory]
    [InlineData("declaration.missing", 403, "Request denied")]
    [InlineData("authorization.forbidden", 403, "Request denied")]
    [InlineData("authorization.missing-permission", 403, "Request denied")]
    [InlineData("authorization.unknown-policy", 403, "Request denied")]
    [InlineData("authorization.not-signed-in", 401, "Not signed in")]
    [InlineData("tenancy.required", 403, "Request denied")]
    [InlineData("validation.failed", 422, "Request validation failed")]
    [InlineData("validation.amount.negative", 422, "Request validation failed")]
    [InlineData("concurrency.conflict", 409, "Concurrency conflict")]
    public void Resolve_KnownCode_ReturnsDefaultMapping(string code, int statusCode, string title)
    {
        var catalog = RefusalProblemCatalog.CreateDefault();

        var mapping = catalog.Resolve(code);

        Assert.Equal(statusCode, mapping.StatusCode);
        Assert.Equal(title, mapping.Title);
    }

    [Fact]
    public void Resolve_IdempotencyCodes_PreservesExistingMappings()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();

        var inFlight = catalog.Resolve(IdempotencyRefusalCodes.InFlight);
        var mismatch = catalog.Resolve(IdempotencyRefusalCodes.FingerprintMismatch);

        Assert.Equal(409, inFlight.StatusCode);
        Assert.Equal("Idempotent request in flight", inFlight.Title);
        Assert.Equal(422, mismatch.StatusCode);
        Assert.Equal("Idempotency key already used", mismatch.Title);
    }

    [Theory]
    [InlineData("billing.unpaid")]
    [InlineData("authorization")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_UnknownCode_ReturnsBadRequestFallback(string? code)
    {
        var catalog = RefusalProblemCatalog.CreateDefault();

        var mapping = catalog.Resolve(code);

        Assert.Equal(400, mapping.StatusCode);
        Assert.Equal("Invalid request", mapping.Title);
    }

    [Fact]
    public void Resolve_ExactMapping_WinsOverPrefixPattern()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("authorization.special", 409, "Special conflict");

        var mapping = catalog.Resolve("authorization.special");

        Assert.Equal(409, mapping.StatusCode);
        Assert.Equal("Special conflict", mapping.Title);
    }

    [Fact]
    public void Resolve_LongestPrefix_WinsWhenSeveralPatternsCoverOneCode()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Map("billing.*", 402, "Billing required");
        catalog.Map("billing.tax.*", 422, "Tax validation failed");

        var mapping = catalog.Resolve("billing.tax.missing");

        Assert.Equal(422, mapping.StatusCode);
        Assert.Equal("Tax validation failed", mapping.Title);
    }

    [Fact]
    public void Map_CustomCode_OverridesDefaultMapping()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("tenancy.required", 401, "Tenant sign-in required");

        var mapping = catalog.Resolve("tenancy.required");

        Assert.Equal(401, mapping.StatusCode);
        Assert.Equal("Tenant sign-in required", mapping.Title);
    }

    [Fact]
    public void Map_ExactCode_DoesNotMatchLongerCodes()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Map("billing.unpaid", 402, "Billing required");

        var mapping = catalog.Resolve("billing.unpaid.line");

        Assert.Equal(400, mapping.StatusCode);
    }

    [Fact]
    public void Map_PrefixPattern_DoesNotMatchTheBarePrefix()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Map("billing.*", 402, "Billing required");

        var mapping = catalog.Resolve("billing");

        Assert.Equal(400, mapping.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Map_BlankCode_Throws(string? code)
    {
        var catalog = new RefusalProblemCatalog();

        Assert.ThrowsAny<ArgumentException>(() => catalog.Map(code!, 400, "Title"));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void Map_StatusCodeOutsideRange_Throws(int statusCode)
    {
        var catalog = new RefusalProblemCatalog();

        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.Map("billing.unpaid", statusCode, "Title"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Map_BlankTitle_Throws(string? title)
    {
        var catalog = new RefusalProblemCatalog();

        Assert.ThrowsAny<ArgumentException>(() => catalog.Map("billing.unpaid", 402, title!));
    }

    [Theory]
    [InlineData("billing*")]
    [InlineData("*")]
    [InlineData(".*")]
    [InlineData("billing.*.unpaid")]
    public void Map_MalformedPattern_Throws(string code)
    {
        var catalog = new RefusalProblemCatalog();

        Assert.Throws<ArgumentException>(() => catalog.Map(code, 402, "Title"));
    }
}
