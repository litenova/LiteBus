using System.Text.Json;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Version8;

public sealed class RefusalProblemHttpTests
{
    private const string DetailProperty = "detail";

    [Theory]
    [InlineData("declaration.missing", 403, "Request denied", "urn:litebus:declaration:missing")]
    [InlineData("authorization.forbidden", 403, "Request denied", "urn:litebus:authorization:forbidden")]
    [InlineData("authorization.not-signed-in", 401, "Not signed in", "urn:litebus:authorization:not-signed-in")]
    [InlineData("tenancy.required", 403, "Request denied", "urn:litebus:tenancy:required")]
    [InlineData("validation.failed", 422, "Request validation failed", "urn:litebus:validation:failed")]
    [InlineData("concurrency.conflict", 409, "Concurrency conflict", "urn:litebus:concurrency:conflict")]
    [InlineData("billing.unpaid", 400, "Invalid request", "urn:litebus:billing:unpaid")]
    public async Task ToHttpResult_DeniedRefusal_WritesCatalogProblem(
        string code,
        int statusCode,
        string title,
        string type)
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Denied("the message was refused", code);

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(statusCode, httpContext.Response.StatusCode);
        Assert.Equal(statusCode, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(type, problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("the message was refused", problem.RootElement.GetProperty(DetailProperty).GetString());
        Assert.Equal("/commands", problem.RootElement.GetProperty("instance").GetString());
        Assert.Equal(httpContext.TraceIdentifier, problem.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task ToHttpResult_InvalidOutcomeWithCode_WritesCatalogProblem()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Invalid(
            "the amount must be positive",
            "validation.amount.negative",
            [new ValidationFailure("the amount must be positive", "Amount")]);

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(422, httpContext.Response.StatusCode);
        Assert.Equal("Request validation failed", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:validation:amount:negative", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("the amount must be positive", problem.RootElement.GetProperty(DetailProperty).GetString());
    }

    [Fact]
    public async Task ToHttpResult_DenialWithoutCode_WritesDeniedFallbackProblem()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Denied("the message was refused", code: null);

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(400, httpContext.Response.StatusCode);
        Assert.Equal("Invalid request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:mediation:denied", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("the message was refused", problem.RootElement.GetProperty(DetailProperty).GetString());
    }

    [Fact]
    public async Task ToHttpResult_InvalidOutcomeWithoutCode_WritesInvalidFallbackProblem()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Invalid("the request failed", code: null, failures: []);

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(400, httpContext.Response.StatusCode);
        Assert.Equal("Invalid request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:mediation:invalid", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("the request failed", problem.RootElement.GetProperty(DetailProperty).GetString());
    }

    [Fact]
    public async Task ToHttpResult_InFlightKey_KeepsConflictDetail()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Denied("the command is still being applied", IdempotencyRefusalCodes.InFlight);

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(409, httpContext.Response.StatusCode);
        Assert.Equal("Idempotent request in flight", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:idempotency:in-flight", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "A request with the same idempotency key is still being processed. Retry the request.",
            problem.RootElement.GetProperty(DetailProperty).GetString());
    }

    [Fact]
    public async Task ToHttpResult_FingerprintMismatch_KeepsUnprocessableDetail()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Denied("the command was already applied", IdempotencyRefusalCodes.FingerprintMismatch);

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(422, httpContext.Response.StatusCode);
        Assert.Equal("Idempotency key already used", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:idempotency:fingerprint-mismatch", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "The idempotency key was already used for a different request.",
            problem.RootElement.GetProperty(DetailProperty).GetString());
    }

    [Fact]
    public async Task ToHttpResult_CallerCatalog_OverridesDefaultMapping()
    {
        var httpContext = CreateHttpContext();
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required");
        var result = MediationResult.Denied("the account is unpaid", "billing.unpaid");

        var response = result.ToHttpResult(httpContext, () => Results.Ok(), catalog);
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(402, httpContext.Response.StatusCode);
        Assert.Equal("Billing required", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:billing:unpaid", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToHttpResult_RequestServicesCatalog_IsUsedWhenNoCallerCatalogGiven()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required");
        var httpContext = CreateHttpContext(catalog);
        var result = MediationResult.Denied("the account is unpaid", "billing.unpaid");

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(402, httpContext.Response.StatusCode);
        Assert.Equal("Billing required", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ToHttpResult_GenericDenial_WritesCatalogProblem()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult<string>.Denied("sign in first", "authorization.not-signed-in");

        var response = result.ToHttpResult(httpContext, value => Results.Ok(value));
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(401, httpContext.Response.StatusCode);
        Assert.Equal("Not signed in", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:authorization:not-signed-in", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToHttpResult_Success_RunsOnSuccess()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Succeeded();

        var response = result.ToHttpResult(httpContext, () => Results.Ok(new { answered = false }));
        using var payload = await ExecuteToJsonAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(200, httpContext.Response.StatusCode);
        Assert.False(payload.RootElement.GetProperty("answered").GetBoolean());
    }

    [Fact]
    public async Task ToHttpResult_GenericSuccess_PassesValueToOnSuccess()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult<string>.Succeeded("receipt-1");

        var response = result.ToHttpResult(httpContext, value => Results.Ok(new { receipt = value }));
        using var payload = await ExecuteToJsonAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(200, httpContext.Response.StatusCode);
        Assert.Equal("receipt-1", payload.RootElement.GetProperty("receipt").GetString());
    }

    private static DefaultHttpContext CreateHttpContext(IRefusalProblemCatalog? catalog = null)
    {
        var services = new ServiceCollection();

        if (catalog is not null)
        {
            services.AddSingleton(catalog);
        }

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        httpContext.Request.Path = "/commands";

        return httpContext;
    }

    private static async Task<JsonDocument> ExecuteToProblemAsync(IResult result, HttpContext httpContext)
    {
        var document = await ExecuteToJsonAsync(result, httpContext).ConfigureAwait(false);

        Assert.Equal("application/problem+json", httpContext.Response.ContentType);

        return document;
    }

    private static async Task<JsonDocument> ExecuteToJsonAsync(IResult result, HttpContext httpContext)
    {
        using var body = new MemoryStream();
        httpContext.Response.Body = body;
        await result.ExecuteAsync(httpContext).ConfigureAwait(false);

        body.Seek(0, SeekOrigin.Begin);

        return await JsonDocument.ParseAsync(body).ConfigureAwait(false);
    }
}
