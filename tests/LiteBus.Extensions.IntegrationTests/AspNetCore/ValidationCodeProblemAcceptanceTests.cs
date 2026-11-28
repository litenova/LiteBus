using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Extensions.IntegrationTests.AspNetCore;

/// <summary>
///     HTTP acceptance tests proving that strict denial mapping and lenient validation codes coexist.
/// </summary>
public sealed class ValidationCodeProblemAcceptanceTests
{
    /// <summary>
    ///     The denied refusal code with no catalog mapping.
    /// </summary>
    private const string UnmappedDeniedCode = "billing.unpaid";

    /// <summary>
    ///     The single short validation code with no catalog mapping.
    /// </summary>
    private const string ShortValidationCode = "client-required";

    /// <summary>
    ///     The reason reported for the denied refusal.
    /// </summary>
    private const string DeniedReason = "the account is unpaid";

    /// <summary>
    ///     The member the single validation failure applies to.
    /// </summary>
    private const string ValidationMember = "Client";

    /// <summary>
    ///     The message the single validation failure carries.
    /// </summary>
    private const string ValidationMessage = "Client is required.";

    /// <summary>
    ///     The problem detail property name in a serialized problem response.
    /// </summary>
    private const string DetailProperty = "detail";

    /// <summary>
    ///     Verifies that an unmapped denied code answers a server error on the result path.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Get_WhenDeniedResultCodeIsUnmapped_AnswersInternalServerError()
    {
        using var host = await CreateHostAsync().ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/denied-result").ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        problem.GetProperty("status").GetInt32().Should().Be(500);
        problem.GetProperty("title").GetString().Should().Be("Internal server error");
        problem.GetProperty("type").GetString().Should().Be("about:blank");
        problem.GetProperty("code").GetString().Should().Be(UnmappedDeniedCode);
        problem.GetProperty(DetailProperty).GetString().Should().Be(DeniedReason);
    }

    /// <summary>
    ///     Verifies that a single short validation code answers the invalid default on the result path.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Get_WhenInvalidResultCarriesOneShortCode_AnswersInvalidDefault()
    {
        using var host = await CreateHostAsync().ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/invalid-result").ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        problem.GetProperty("status").GetInt32().Should().Be(400);
        problem.GetProperty("title").GetString().Should().Be("Invalid request");
        problem.GetProperty("type").GetString().Should().Be("urn:litebus:client-required");
        problem.GetProperty("code").GetString().Should().Be(ShortValidationCode);
        problem.GetProperty("errors").GetProperty(ValidationMember)[0].GetString().Should().Be(ValidationMessage);
    }

    /// <summary>
    ///     Verifies that an unmapped denied code answers a server error on the exception path.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Get_WhenDeniedExceptionCodeIsUnmapped_AnswersInternalServerError()
    {
        using var host = await CreateHostAsync().ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/denied-throw").ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        problem.GetProperty("status").GetInt32().Should().Be(500);
        problem.GetProperty("title").GetString().Should().Be("Internal server error");
        problem.GetProperty("type").GetString().Should().Be("about:blank");
        problem.GetProperty("code").GetString().Should().Be(UnmappedDeniedCode);
        problem.GetProperty(DetailProperty).GetString().Should().Be(DeniedReason);
    }

    /// <summary>
    ///     Verifies that a single short validation code answers the invalid default on the exception path.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Get_WhenInvalidExceptionCarriesOneShortCode_AnswersInvalidDefault()
    {
        using var host = await CreateHostAsync().ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/invalid-throw").ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        problem.GetProperty("status").GetInt32().Should().Be(400);
        problem.GetProperty("title").GetString().Should().Be("Invalid request");
        problem.GetProperty("type").GetString().Should().Be("urn:litebus:client-required");
        problem.GetProperty("code").GetString().Should().Be(ShortValidationCode);
        problem.GetProperty("errors").GetProperty(ValidationMember)[0].GetString().Should().Be(ValidationMessage);
    }

    /// <summary>
    ///     Builds and starts a test host with strict denial mapping and lenient validation codes.
    /// </summary>
    /// <returns>The started host.</returns>
    private static Task<IHost> CreateHostAsync()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.MapInvalid(StatusCodes.Status400BadRequest, "Invalid request");

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IRefusalProblemCatalog>(catalog);
                    services.AddSingleton(new RefusalProblemOptions
                    {
                        UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError,
                        UnmappedValidationCode = UnmappedValidationCode.UseFallback,
                    });
                    services.AddProblemDetails();
                    services.AddLiteBusExceptionHandler();
                });

                webBuilder.Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/denied-result", WriteDeniedResult);
                        endpoints.MapGet("/invalid-result", WriteInvalidResult);
                        endpoints.MapGet("/denied-throw", ThrowDenied);
                        endpoints.MapGet("/invalid-throw", ThrowInvalid);
                    });
                });
            });

        return builder.StartAsync();
    }

    /// <summary>
    ///     Answers the denied result for one unmapped code as a problem response.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <returns>The problem response the catalog maps to.</returns>
    private static IResult WriteDeniedResult(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = MediationResult.Denied(DeniedReason, UnmappedDeniedCode);

        return result.ToHttpResult(context, () => Results.Ok());
    }

    /// <summary>
    ///     Answers the invalid result for one short validation code as a problem response.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <returns>The problem response the catalog maps to.</returns>
    private static IResult WriteInvalidResult(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<ValidationFailure> failures =
            [new ValidationFailure(ValidationMessage, ValidationMember, ShortValidationCode)];
        var result = MediationResult.Invalid($"{ValidationMember}: {ValidationMessage}", ShortValidationCode, failures);

        return result.ToHttpResult(context, () => Results.Ok());
    }

    /// <summary>
    ///     Raises the denied exception for one unmapped code.
    /// </summary>
    private static void ThrowDenied()
    {
        throw new LiteBusMessageDeniedException(typeof(SampleMessage), DeniedReason, UnmappedDeniedCode);
    }

    /// <summary>
    ///     Raises the invalid exception for one short validation code.
    /// </summary>
    private static void ThrowInvalid()
    {
        IReadOnlyList<ValidationFailure> failures =
            [new ValidationFailure(ValidationMessage, ValidationMember, ShortValidationCode)];

        throw new LiteBusMessageInvalidException(typeof(SampleMessage), failures);
    }

    /// <summary>
    ///     A sample message type carried by the mediation exceptions the endpoints raise.
    /// </summary>
    private sealed class SampleMessage;
}
