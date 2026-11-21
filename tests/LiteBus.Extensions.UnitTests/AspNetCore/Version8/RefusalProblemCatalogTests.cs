using System.Text.Json;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

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
    public void Map_ConflictingRemap_ThrowsNamingCodeAndBothMappings()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();

        var exception = Assert.Throws<LiteBusConfigurationException>(
            () => catalog.Map("tenancy.required", 401, "Tenant sign-in required", "Tenancy"));

        Assert.Contains("tenancy.required", exception.Message);
        Assert.Contains("403", exception.Message);
        Assert.Contains("Request denied", exception.Message);
        Assert.Contains("LiteBus", exception.Message);
        Assert.Contains("401", exception.Message);
        Assert.Contains("Tenant sign-in required", exception.Message);
        Assert.Contains("Tenancy", exception.Message);
        Assert.Contains("Replace", exception.Message);
    }

    [Fact]
    public void Map_ConflictingRemapWithoutContributors_ThrowsNamingCodeAndBothMappings()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Map("billing.unpaid", 402, "Billing required");

        var exception = Assert.Throws<LiteBusConfigurationException>(
            () => catalog.Map("billing.unpaid", 409, "Billing conflict"));

        Assert.Contains("billing.unpaid", exception.Message);
        Assert.Contains("402", exception.Message);
        Assert.Contains("409", exception.Message);
    }

    [Fact]
    public void Map_IdenticalRepeat_IsNoOpKeepingFirstContributor()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Map("billing.unpaid", 402, "Billing required", "Billing");
        catalog.Map("billing.unpaid", 402, "Billing required", "Checkout");

        var mapping = catalog.Resolve("billing.unpaid");

        Assert.Equal(402, mapping.StatusCode);
        Assert.Equal("Billing required", mapping.Title);
        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("Billing", entry.Contributor);
    }

    [Fact]
    public void Replace_ExistingMapping_OverridesMapping()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Replace("tenancy.required", 401, "Tenant sign-in required", "Tenancy");

        var mapping = catalog.Resolve("tenancy.required");

        Assert.Equal(401, mapping.StatusCode);
        Assert.Equal("Tenant sign-in required", mapping.Title);
        Assert.Equal("Tenancy", catalog.Entries.First(entry => entry.Pattern == "tenancy.required").Contributor);
    }

    [Fact]
    public void Replace_NewPattern_AddsMapping()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Replace("billing.unpaid", 402, "Billing required", "Billing");

        var mapping = catalog.Resolve("billing.unpaid");

        Assert.Equal(402, mapping.StatusCode);
        Assert.Equal("Billing required", mapping.Title);
    }

    [Fact]
    public void MapInvalid_SetsInvalidDefaultUsedWithoutCodeMapping()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.MapInvalid(422, "Semantic errors");

        Assert.Equal(422, catalog.Resolve("billing.unpaid", MediationOutcome.Invalid).StatusCode);
        Assert.Equal("Semantic errors", catalog.Resolve(null, MediationOutcome.Invalid).Title);
        Assert.Equal(422, catalog.Resolve("validation.failed", MediationOutcome.Invalid).StatusCode);
    }

    [Fact]
    public void MapDenied_SetsDeniedDefaultUsedWithoutCodeMapping()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.MapDenied(403, "Request denied");

        var mapping = catalog.Resolve("billing.unpaid", MediationOutcome.Denied);

        Assert.Equal(403, mapping.StatusCode);
        Assert.Equal("Request denied", mapping.Title);
    }

    [Fact]
    public void MapInvalid_ReplacesPreviousDefault()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.MapInvalid(422, "Semantic errors");
        catalog.MapInvalid(400, "Invalid request");

        var mapping = catalog.Resolve(null, MediationOutcome.Invalid);

        Assert.Equal(400, mapping.StatusCode);
        Assert.Equal("Invalid request", mapping.Title);
    }

    [Fact]
    public void Entries_ListsEveryMappingWithItsContributorOrderedByPattern()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required", "Billing");

        var entries = catalog.Entries;

        Assert.Equal(9, entries.Count);
        Assert.Equal(entries.OrderBy(entry => entry.Pattern, StringComparer.Ordinal).Select(entry => entry.Pattern), entries.Select(entry => entry.Pattern));
        var billing = entries.First(entry => entry.Pattern == "billing.unpaid");
        Assert.Equal(402, billing.StatusCode);
        Assert.Equal("Billing required", billing.Title);
        Assert.Equal("Billing", billing.Contributor);
        Assert.All(entries.Where(entry => entry.Pattern != "billing.unpaid"), entry => Assert.Equal("LiteBus", entry.Contributor));
    }

    [Fact]
    public void Export_ListsMappingsAndOutcomeDefaults()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required", "Billing");
        catalog.MapInvalid(422, "Semantic errors");

        var export = catalog.Export();

        Assert.Equal(9, export.Mappings.Count);
        Assert.Contains(export.Mappings, entry => entry.Pattern == "billing.unpaid" && entry.Contributor == "Billing");
        Assert.Equal(422, export.InvalidDefault.StatusCode);
        Assert.Equal("Semantic errors", export.InvalidDefault.Title);
        Assert.Equal(400, export.DeniedDefault.StatusCode);
    }

    [Fact]
    public void ExportJson_ListsEveryCodeWithMappingAndContributor()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required", "Billing");
        catalog.MapDenied(403, "Request denied");

        using var export = JsonDocument.Parse(catalog.ExportJson());

        var mappings = export.RootElement.GetProperty("mappings");
        Assert.Equal(9, mappings.GetArrayLength());
        var billing = mappings.EnumerateArray().First(entry => entry.GetProperty("pattern").GetString() == "billing.unpaid");
        Assert.Equal(402, billing.GetProperty("statusCode").GetInt32());
        Assert.Equal("Billing required", billing.GetProperty("title").GetString());
        Assert.Equal("Billing", billing.GetProperty("contributor").GetString());

        var invalidDefault = export.RootElement.GetProperty("invalidDefault");
        Assert.Equal(400, invalidDefault.GetProperty("statusCode").GetInt32());
        var deniedDefault = export.RootElement.GetProperty("deniedDefault");
        Assert.Equal(403, deniedDefault.GetProperty("statusCode").GetInt32());
        Assert.Equal("Request denied", deniedDefault.GetProperty("title").GetString());
    }

    [Fact]
    public void ExportJson_FeedOpenApiResponseExamples()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required", "Billing");

        using var export = JsonDocument.Parse(catalog.ExportJson());
        Dictionary<int, List<string>> examples = [];

        foreach (var entry in export.RootElement.GetProperty("mappings").EnumerateArray())
        {
            var statusCode = entry.GetProperty("statusCode").GetInt32();
            var title = entry.GetProperty("title").GetString();

            if (!examples.TryGetValue(statusCode, out var titles))
            {
                titles = [];
                examples[statusCode] = titles;
            }

            titles.Add(title!);
        }

        Assert.Contains("Billing required", examples[402]);
        Assert.Contains("Request denied", examples[403]);
        Assert.Contains("Request validation failed", examples[422]);
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
