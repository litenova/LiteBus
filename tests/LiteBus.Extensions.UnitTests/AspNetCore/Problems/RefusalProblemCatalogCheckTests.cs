using LiteBus.Extensions.AspNetCore;
using LiteBus.Runtime.Abstractions.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Problems;

public sealed class RefusalProblemCatalogCheckTests
{
    [Fact]
    public void Name_IsProblemCatalogProbe()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var check = new RefusalProblemCatalogDiagnosticCheck(provider);

        Assert.Equal("litebus.problem-catalog", check.Name);
        Assert.Equal(RefusalProblemCatalogDiagnosticCheck.CheckName, check.Name);
    }

    [Fact]
    public async Task CheckAsync_WithoutCatalog_ReportsHealthyWithDefaultCatalog()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var check = new RefusalProblemCatalogDiagnosticCheck(provider);

        var result = await check.CheckAsync().ConfigureAwait(false);

        Assert.Equal(DiagnosticStatus.Healthy, result.Status);
        Assert.NotNull(result.Data);
        Assert.False((bool) result.Data!["registered"]);
    }

    [Fact]
    public async Task CheckAsync_WithCatalog_ListsMappingsContributorsAndOutcomeDefaults()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required", "Billing");
        catalog.MapInvalid(400, "Invalid request");
        catalog.MapDenied(403, "Request denied");
        using var provider = new ServiceCollection().AddSingleton<IRefusalProblemCatalog>(catalog).BuildServiceProvider();
        var check = new RefusalProblemCatalogDiagnosticCheck(provider);

        var result = await check.CheckAsync().ConfigureAwait(false);

        Assert.Equal(DiagnosticStatus.Healthy, result.Status);
        Assert.NotNull(result.Data);
        Assert.Equal(9, (int) result.Data!["mappingCount"]);
        Assert.Contains("billing.unpaid", Assert.IsAssignableFrom<string[]>(result.Data!["patterns"]));
        var contributors = Assert.IsAssignableFrom<string[]>(result.Data!["contributors"]);
        Assert.Contains("Billing", contributors);
        Assert.Contains("LiteBus", contributors);
        Assert.Equal("400 Invalid request", (string) result.Data["invalidDefault"]);
        Assert.Equal("403 Request denied", (string) result.Data["deniedDefault"]);
    }

    [Fact]
    public async Task CheckAsync_WithCustomCatalog_ReportsHealthyWithoutExport()
    {
        using var provider = new ServiceCollection().AddSingleton<IRefusalProblemCatalog>(new CustomCatalog()).BuildServiceProvider();
        var check = new RefusalProblemCatalogDiagnosticCheck(provider);

        var result = await check.CheckAsync().ConfigureAwait(false);

        Assert.Equal(DiagnosticStatus.Healthy, result.Status);
        Assert.NotNull(result.Data);
        Assert.True((bool) result.Data!["customCatalog"]);
    }

    private sealed class CustomCatalog : IRefusalProblemCatalog
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
