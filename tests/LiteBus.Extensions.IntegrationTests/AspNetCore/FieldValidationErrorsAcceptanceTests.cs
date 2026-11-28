using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Extensions.IntegrationTests.AspNetCore;

/// <summary>
///     HTTP acceptance tests proving that the object-per-field validation writer answers per-field failures as a
///     <c>fields</c> array of entries carrying the member, the code, and the message.
/// </summary>
public sealed class FieldValidationErrorsAcceptanceTests
{
    /// <summary>
    ///     Verifies that an invalid result answered through the field writer carries one entry per failure with the
    ///     member, the code, and the message.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Get_InvalidResultWithFieldWriter_WritesFieldsArrayWithFieldCodeAndMessage()
    {
        IReadOnlyList<ValidationFailure> failures =
        [
            new ValidationFailure("Amount must be positive.", "Amount", "validation.amount.negative"),
            new ValidationFailure("The order total does not match its lines.")
        ];
        var result = MediationResult.Invalid(
            "Amount: Amount must be positive.; The order total does not match its lines.",
            "validation.amount.negative",
            failures);
        var options = new RefusalProblemOptions { ValidationErrors = new FieldValidationErrorsWriter() };

        using var host = await CreateHostAsync(
            mapEndpoints: endpoints => endpoints.MapGet(
                "/validate",
                (HttpContext context) => result.ToHttpResult(context, () => Results.Ok(), options: options))).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/validate").ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        var fields = body.GetProperty("fields");
        fields.GetArrayLength().Should().Be(2);

        fields[0].GetProperty("field").GetString().Should().Be("Amount");
        fields[0].GetProperty("code").GetString().Should().Be("validation.amount.negative");
        fields[0].GetProperty("message").GetString().Should().Be("Amount must be positive.");

        fields[1].GetProperty("field").ValueKind.Should().Be(JsonValueKind.Null);
        fields[1].GetProperty("code").ValueKind.Should().Be(JsonValueKind.Null);
        fields[1].GetProperty("message").GetString().Should().Be("The order total does not match its lines.");

        body.TryGetProperty("errors", out _).Should().BeFalse();
        body.TryGetProperty("errorCodes", out _).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that the field writer answers under the configured extension and entry member names.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Get_FieldWriterWithCustomMemberNames_WritesConfiguredExtensionAndMembers()
    {
        IReadOnlyList<ValidationFailure> failures =
        [
            new ValidationFailure("Amount must be positive.", "Amount", "validation.amount.negative")
        ];
        var result = MediationResult.Invalid(
            "Amount: Amount must be positive.",
            "validation.amount.negative",
            failures);
        var options = new RefusalProblemOptions
        {
            ValidationErrors = new FieldValidationErrorsWriter(new FieldValidationErrorsWriterOptions
            {
                ExtensionName = "fieldErrors",
                FieldMemberName = "name",
                CodeMemberName = "errorCode",
                MessageMemberName = "text",
            }),
        };

        using var host = await CreateHostAsync(
            mapEndpoints: endpoints => endpoints.MapGet(
                "/validate",
                (HttpContext context) => result.ToHttpResult(context, () => Results.Ok(), options: options))).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/validate").ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        var fieldErrors = body.GetProperty("fieldErrors");
        fieldErrors.GetArrayLength().Should().Be(1);

        fieldErrors[0].GetProperty("name").GetString().Should().Be("Amount");
        fieldErrors[0].GetProperty("errorCode").GetString().Should().Be("validation.amount.negative");
        fieldErrors[0].GetProperty("text").GetString().Should().Be("Amount must be positive.");

        body.TryGetProperty("fields", out _).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that an invalid exception answered through the exception handler carries the same entries as the
    ///     result path.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Get_InvalidExceptionWithFieldWriter_WritesFieldsArray()
    {
        IReadOnlyList<ValidationFailure> failures =
        [
            new ValidationFailure("Amount must be positive.", "Amount", "validation.amount.negative")
        ];

        using var host = await CreateHostAsync(
            configureServices: services =>
            {
                services.AddProblemDetails();
                services.AddLiteBusExceptionHandler();
                services.AddSingleton(new RefusalProblemOptions
                {
                    ValidationErrors = new FieldValidationErrorsWriter(),
                });
            },
            mapEndpoints: endpoints => endpoints.MapGet(
                "/validate",
                () => { throw new LiteBusMessageInvalidException(typeof(FieldValidationErrorsAcceptanceTests), failures); }),
            useExceptionHandler: true).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/validate").ConfigureAwait(false);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        var fields = body.GetProperty("fields");
        fields.GetArrayLength().Should().Be(1);

        fields[0].GetProperty("field").GetString().Should().Be("Amount");
        fields[0].GetProperty("code").GetString().Should().Be("validation.amount.negative");
        fields[0].GetProperty("message").GetString().Should().Be("Amount must be positive.");
    }

    /// <summary>
    ///     Builds and starts a test host with validation endpoints backed by the in-memory test server.
    /// </summary>
    /// <param name="mapEndpoints">Maps the validation endpoints.</param>
    /// <param name="configureServices">Registers additional services, when the test needs them.</param>
    /// <param name="useExceptionHandler">Whether the pipeline routes exceptions through the handler middleware.</param>
    /// <returns>The started host.</returns>
    private static Task<IHost> CreateHostAsync(
        Action<IEndpointRouteBuilder> mapEndpoints,
        Action<IServiceCollection>? configureServices = null,
        bool useExceptionHandler = false)
    {
        ArgumentNullException.ThrowIfNull(mapEndpoints);

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    configureServices?.Invoke(services);
                });

                webBuilder.Configure(app =>
                {
                    if (useExceptionHandler)
                    {
                        app.UseExceptionHandler();
                    }

                    app.UseRouting();
                    app.UseEndpoints(mapEndpoints);
                });
            });

        return builder.StartAsync();
    }
}
