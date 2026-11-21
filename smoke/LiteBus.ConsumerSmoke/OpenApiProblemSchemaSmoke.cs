using System.Net.Http.Json;
using System.Text.Json;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Extensions.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Builds the OpenAPI document of a billing endpoint and verifies the problem members and refusal codes.
/// </summary>
internal static class OpenApiProblemSchemaSmoke
{
    /// <summary>
    ///     The refusal code mapped to 402 by the smoke catalog.
    /// </summary>
    private const string BillingCode = "billing.unpaid";

    /// <summary>
    ///     Runs the OpenAPI problem schema scenario.
    /// </summary>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddRouting();
        builder.Services.AddEndpointsApiExplorer();

        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map(BillingCode, StatusCodes.Status402PaymentRequired, "Billing required", "Billing");
        builder.Services.AddSingleton<IRefusalProblemCatalog>(catalog);
        builder.Services.AddLiteBusProblemSchema(schema =>
        {
            schema.IncludeRefusalCodes = true;
        });

        var app = builder.Build();
        app.MapGet("/billing", () => Results.Ok("paid"))
            .Produces<string>()
            .ProducesProblem(StatusCodes.Status402PaymentRequired);
        app.MapOpenApi();

        await app.StartAsync().ConfigureAwait(false);

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First(), UriKind.Absolute) };
            using var response = await client.GetAsync("/openapi/v1.json").ConfigureAwait(false);
            Smoke.Expect(response.IsSuccessStatusCode, "the OpenAPI document endpoint answers success");

            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>().ConfigureAwait(false);
            Smoke.Expect(document is not null, "the OpenAPI document parses as JSON");

            var properties = document!.RootElement
                .GetProperty("components")
                .GetProperty("schemas")
                .GetProperty("ProblemDetails")
                .GetProperty("properties");

            Smoke.Expect(
                properties.TryGetProperty("code", out var code)
                    && code.GetProperty("type").GetString() == "string",
                "the problem schema carries the code member");
            Smoke.Expect(
                properties.TryGetProperty("traceId", out var traceId)
                    && traceId.GetProperty("type").GetString() == "string",
                "the problem schema carries the traceId member");
            Smoke.Expect(
                properties.TryGetProperty("errors", out var errors)
                    && errors.GetProperty("type").GetString() == "object"
                    && errors.GetProperty("additionalProperties").GetProperty("type").GetString() == "array"
                    && errors.GetProperty("additionalProperties").GetProperty("items").GetProperty("type").GetString() == "string",
                "the problem schema carries the errors member");

            var codes = document.RootElement
                .GetProperty("paths")
                .GetProperty("/billing")
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("402")
                .GetProperty("x-litebus-refusal-codes");

            Smoke.Expect(
                codes.ValueKind == JsonValueKind.Array
                    && codes.EnumerateArray().Any(element => element.GetString() == BillingCode),
                "the 402 problem response carries the billing refusal code");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }

        await app.DisposeAsync().ConfigureAwait(false);
    }
}
