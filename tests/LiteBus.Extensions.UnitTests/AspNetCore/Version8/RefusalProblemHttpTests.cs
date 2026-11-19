using System.Text.Json;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

    [Fact]
    public async Task ToHttpResult_Precedence_ExactBeatsPrefixBeatsOutcomeDefault()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required");
        catalog.Map("billing.tax.*", 422, "Tax validation failed");
        catalog.Map("billing.*", 409, "Billing conflict");
        catalog.MapDenied(403, "Request denied");

        var exactContext = CreateHttpContext(catalog);
        var exactResponse = MediationResult.Denied("the account is unpaid", "billing.unpaid")
            .ToHttpResult(exactContext, () => Results.Ok());
        using var exact = await ExecuteToProblemAsync(exactResponse, exactContext).ConfigureAwait(false);
        Assert.Equal(402, exactContext.Response.StatusCode);
        Assert.Equal("Billing required", exact.RootElement.GetProperty("title").GetString());

        var prefixContext = CreateHttpContext(catalog);
        var prefixResponse = MediationResult.Denied("the tax form is missing", "billing.tax.missing")
            .ToHttpResult(prefixContext, () => Results.Ok());
        using var prefix = await ExecuteToProblemAsync(prefixResponse, prefixContext).ConfigureAwait(false);
        Assert.Equal(422, prefixContext.Response.StatusCode);
        Assert.Equal("Tax validation failed", prefix.RootElement.GetProperty("title").GetString());

        var shortPrefixContext = CreateHttpContext(catalog);
        var shortPrefixResponse = MediationResult.Denied("the account is disputed", "billing.disputed")
            .ToHttpResult(shortPrefixContext, () => Results.Ok());
        using var shortPrefix = await ExecuteToProblemAsync(shortPrefixResponse, shortPrefixContext).ConfigureAwait(false);
        Assert.Equal(409, shortPrefixContext.Response.StatusCode);
        Assert.Equal("Billing conflict", shortPrefix.RootElement.GetProperty("title").GetString());

        var outcomeContext = CreateHttpContext(catalog);
        var outcomeResponse = MediationResult.Denied("the request is refused", "shipping.blocked")
            .ToHttpResult(outcomeContext, () => Results.Ok());
        using var outcome = await ExecuteToProblemAsync(outcomeResponse, outcomeContext).ConfigureAwait(false);
        Assert.Equal(403, outcomeContext.Response.StatusCode);
        Assert.Equal("Request denied", outcome.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:shipping:blocked", outcome.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToHttpResult_InvalidWithZeroOneAndTwoFailures_AnswersIdenticalInvalidDefault()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.MapInvalid(400, "Invalid request");

        var emptyContext = CreateHttpContext(catalog);
        var emptyResponse = MediationResult.Invalid("the request failed", code: null, failures: [])
            .ToHttpResult(emptyContext, () => Results.Ok());
        using var empty = await ExecuteToProblemAsync(emptyResponse, emptyContext).ConfigureAwait(false);

        var singleUncodedContext = CreateHttpContext(catalog);
        var singleUncodedResponse = MediationResult.Invalid(
                "Amount: Amount must be positive",
                code: null,
                [new ValidationFailure("Amount must be positive", "Amount")])
            .ToHttpResult(singleUncodedContext, () => Results.Ok());
        using var singleUncoded = await ExecuteToProblemAsync(singleUncodedResponse, singleUncodedContext).ConfigureAwait(false);

        var singleCodedContext = CreateHttpContext(catalog);
        var singleCodedResponse = MediationResult.Invalid(
                "Amount: Amount must be positive",
                "billing.unpaid",
                [new ValidationFailure("Amount must be positive", "Amount", "billing.unpaid")])
            .ToHttpResult(singleCodedContext, () => Results.Ok());
        using var singleCoded = await ExecuteToProblemAsync(singleCodedResponse, singleCodedContext).ConfigureAwait(false);

        var severalContext = CreateHttpContext(catalog);
        var severalResponse = MediationResult.Invalid(
                "Amount: Amount must be positive; Currency: Currency is required",
                code: null,
                [
                    new ValidationFailure("Amount must be positive", "Amount"),
                    new ValidationFailure("Currency is required", "Currency")
                ])
            .ToHttpResult(severalContext, () => Results.Ok());
        using var several = await ExecuteToProblemAsync(severalResponse, severalContext).ConfigureAwait(false);

        (DefaultHttpContext Context, JsonDocument Problem)[] cases =
        [
            (emptyContext, empty), (singleUncodedContext, singleUncoded),
            (singleCodedContext, singleCoded), (severalContext, several)
        ];

        foreach (var (context, problem) in cases)
        {
            Assert.Equal(400, context.Response.StatusCode);
            Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Invalid request", problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task ToHttpResult_UnmappedCodeWithUseFallback_WritesOutcomeDefault()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Denied("the account is unpaid", "billing.unpaid");

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(400, httpContext.Response.StatusCode);
        Assert.Equal("Invalid request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:billing:unpaid", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("the account is unpaid", problem.RootElement.GetProperty(DetailProperty).GetString());
    }

    [Fact]
    public async Task ToHttpResult_UnmappedCodeWithTreatAsServerError_WritesGenericTypeAndLogs()
    {
        var logger = new CapturingCatalogLogger();
        var httpContext = CreateHttpContext(
            options: new RefusalProblemOptions { UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError },
            catalogLogger: logger);
        var result = MediationResult.Denied("the account is unpaid", "billing.unpaid");

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(500, httpContext.Response.StatusCode);
        Assert.Equal("Internal server error", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("about:blank", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("billing.unpaid", problem.RootElement.GetProperty("code").GetString());

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("billing.unpaid", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task ToHttpResult_CallerOptions_OverrideServicesOptions()
    {
        var httpContext = CreateHttpContext(options: new RefusalProblemOptions());
        var result = MediationResult.Denied("the account is unpaid", "billing.unpaid");

        var response = result.ToHttpResult(
            httpContext,
            () => Results.Ok(),
            options: new RefusalProblemOptions { UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError });
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(500, httpContext.Response.StatusCode);
        Assert.Equal("about:blank", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToHttpResult_GenericUnmappedCodeWithTreatAsServerError_WritesInternalServerError()
    {
        var httpContext = CreateHttpContext(
            options: new RefusalProblemOptions { UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError });
        var result = MediationResult<string>.Denied("the account is unpaid", "billing.unpaid");

        var response = result.ToHttpResult(httpContext, value => Results.Ok(value));
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(500, httpContext.Response.StatusCode);
        Assert.Equal("Internal server error", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("about:blank", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToHttpResult_WritesCodeExtensionByDefault()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Denied("the message was refused", "authorization.forbidden");

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal("authorization.forbidden", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ToHttpResult_IncludeRefusalCodeFalse_OmitsCodeExtension()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Denied("the message was refused", "authorization.forbidden");

        var response = result.ToHttpResult(
            httpContext, () => Results.Ok(), options: new RefusalProblemOptions { IncludeRefusalCode = false });
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.False(problem.RootElement.TryGetProperty("code", out _));
        Assert.Equal("urn:litebus:authorization:forbidden", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToHttpResult_DocumentationUrlFormatter_WritesTemplateType()
    {
        var httpContext = CreateHttpContext();
        var options = new RefusalProblemOptions
        {
            TypeFormatter = new DocumentationUrlProblemTypeFormatter("https://errors.example.com/{code}"),
        };
        var result = MediationResult.Denied("the message was refused", "authorization.forbidden");

        var response = result.ToHttpResult(httpContext, () => Results.Ok(), options: options);
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(403, httpContext.Response.StatusCode);
        Assert.Equal("https://errors.example.com/authorization.forbidden", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("Request denied", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ToHttpResult_AboutBlankFormatter_WritesAboutBlankKeepingMappedTitle()
    {
        var httpContext = CreateHttpContext();
        var options = new RefusalProblemOptions { TypeFormatter = AboutBlankProblemTypeFormatter.Default };
        var result = MediationResult.Denied("the message was refused", "authorization.forbidden");

        var response = result.ToHttpResult(httpContext, () => Results.Ok(), options: options);
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(403, httpContext.Response.StatusCode);
        Assert.Equal("about:blank", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("Request denied", problem.RootElement.GetProperty("title").GetString());
    }

    private static DefaultHttpContext CreateHttpContext(
        IRefusalProblemCatalog? catalog = null,
        RefusalProblemOptions? options = null,
        ILogger<RefusalProblemCatalog>? catalogLogger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (catalog is not null)
        {
            services.AddSingleton(catalog);
        }

        if (options is not null)
        {
            services.AddSingleton(options);
        }

        if (catalogLogger is not null)
        {
            services.AddSingleton(catalogLogger);
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

    private sealed class CapturingCatalogLogger : ILogger<RefusalProblemCatalog>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Shared;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }

        public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

        private sealed class NullScope : IDisposable
        {
            public static NullScope Shared { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
