using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LiteBus.Extensions.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Extensions.IntegrationTests.AspNetCore;

/// <summary>
///     Acceptance tests proving that the generated OpenAPI document carries the problem members LiteBus always
///     sends, and optionally the refusal codes answered at each status code.
/// </summary>
public sealed class OpenApiProblemSchemaAcceptanceTests
{
    /// <summary>
    ///     The refusal code mapped to 402 by the test catalog.
    /// </summary>
    private const string BillingCode = "billing.unpaid";

    /// <summary>
    ///     The response extension holding the refusal codes answered at one status code.
    /// </summary>
    private const string RefusalCodesExtensionName = "x-litebus-refusal-codes";

    /// <summary>
    ///     The route of the generated OpenAPI document.
    /// </summary>
    private const string OpenApiDocumentRoute = "/openapi/v1.json";

    /// <summary>
    ///     Verifies that the problem schema of the generated document carries the code, traceId and errors members.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task GetOpenApiDocument_ProblemSchemaCarriesLiteBusMembers()
    {
        using var host = await CreateHostAsync(includeRefusalCodes: false).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(OpenApiDocumentRoute).ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
        document.Should().NotBeNull();

        var properties = ProblemProperties(document!);
        properties.TryGetProperty("type", out _).Should().BeTrue();
        properties.TryGetProperty("title", out _).Should().BeTrue();
        properties.TryGetProperty("status", out _).Should().BeTrue();

        properties.TryGetProperty("code", out var code).Should().BeTrue();
        code.GetProperty("type").GetString().Should().Be("string");

        properties.TryGetProperty("traceId", out var traceId).Should().BeTrue();
        traceId.GetProperty("type").GetString().Should().Be("string");

        properties.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.GetProperty("type").GetString().Should().Be("object");

        var members = errors.GetProperty("additionalProperties");
        members.GetProperty("type").GetString().Should().Be("array");
        members.GetProperty("items").GetProperty("type").GetString().Should().Be("string");
    }

    /// <summary>
    ///     Verifies that each problem response carries the refusal codes the catalog answers at its status code.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task GetOpenApiDocument_WithRefusalCodes_ProblemResponsesCarryPerStatusCodes()
    {
        using var host = await CreateHostAsync(includeRefusalCodes: true).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(OpenApiDocumentRoute).ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
        document.Should().NotBeNull();

        var responses = BillingResponses(document!);

        var payment = responses.GetProperty("402");
        payment.TryGetProperty(RefusalCodesExtensionName, out var paymentCodes).Should().BeTrue();
        paymentCodes.EnumerateArray().Select(element => element.GetString()).ToList()
            .Should().BeEquivalentTo([BillingCode]);

        var forbidden = responses.GetProperty("403");
        forbidden.TryGetProperty(RefusalCodesExtensionName, out var forbiddenCodes).Should().BeTrue();
        var forbiddenValues = forbiddenCodes.EnumerateArray().Select(element => element.GetString()).ToList();
        forbiddenValues.Should().Contain("declaration.missing");
        forbiddenValues.Should().Contain("authorization.*");
        forbiddenValues.Should().Contain("tenancy.required");

        responses.GetProperty("400").TryGetProperty(RefusalCodesExtensionName, out _).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that problem responses carry no refusal codes unless the host opts in.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task GetOpenApiDocument_WithoutRefusalCodes_ProblemResponsesCarryNoCodes()
    {
        using var host = await CreateHostAsync(includeRefusalCodes: false).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(OpenApiDocumentRoute).ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
        document.Should().NotBeNull();

        var responses = BillingResponses(document!);
        responses.GetProperty("402").TryGetProperty(RefusalCodesExtensionName, out _).Should().BeFalse();
        responses.GetProperty("403").TryGetProperty(RefusalCodesExtensionName, out _).Should().BeFalse();
    }

    /// <summary>
    ///     Builds and starts a test host serving the OpenAPI document of one billing endpoint.
    /// </summary>
    /// <param name="includeRefusalCodes">Whether problem responses carry per-status refusal codes.</param>
    /// <returns>The started host.</returns>
    private static Task<IHost> CreateHostAsync(bool includeRefusalCodes)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddEndpointsApiExplorer();

                    var catalog = RefusalProblemCatalog.CreateDefault();
                    catalog.Map(BillingCode, StatusCodes.Status402PaymentRequired, "Billing required", "Billing");
                    services.AddSingleton<IRefusalProblemCatalog>(catalog);

                    services.AddLiteBusProblemSchema(schema =>
                    {
                        schema.IncludeRefusalCodes = includeRefusalCodes;
                    });
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/billing", () => Results.Ok("paid"))
                            .Produces<string>()
                            .ProducesProblem(StatusCodes.Status400BadRequest)
                            .ProducesProblem(StatusCodes.Status402PaymentRequired)
                            .ProducesProblem(StatusCodes.Status403Forbidden);
                        endpoints.MapOpenApi();
                    });
                });
            });

        return builder.StartAsync();
    }

    /// <summary>
    ///     Reads the properties of the ProblemDetails schema from a generated document.
    /// </summary>
    /// <param name="document">The generated OpenAPI document.</param>
    /// <returns>The schema properties.</returns>
    private static JsonElement ProblemProperties(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ProblemDetails")
            .GetProperty("properties");
    }

    /// <summary>
    ///     Reads the responses of the billing operation from a generated document.
    /// </summary>
    /// <param name="document">The generated OpenAPI document.</param>
    /// <returns>The operation responses by status code.</returns>
    private static JsonElement BillingResponses(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.RootElement
            .GetProperty("paths")
            .GetProperty("/billing")
            .GetProperty("get")
            .GetProperty("responses");
    }
}
