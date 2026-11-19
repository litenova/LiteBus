using System.Text.Json;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Problems;

public sealed class LiteBusExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_DeniedException_WritesSameProblemAsToHttpResult()
    {
        const string reason = "the message was refused";
        const string code = "authorization.forbidden";
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), NullLogger<LiteBusExceptionHandler>.Instance);

        var resultContext = CreateHttpContext();
        var result = MediationResult.Denied(reason, code);
        var resultResponse = result.ToHttpResult(resultContext, () => Results.Ok());
        using var expected = await ExecuteToProblemAsync(resultResponse, resultContext).ConfigureAwait(false);

        var handlerContext = CreateHttpContext();
        var exception = new LiteBusMessageDeniedException(typeof(object), reason, code);
        using var actual = await HandleToProblemAsync(handler, handlerContext, exception).ConfigureAwait(false);

        Assert.Equal(
            expected.RootElement.GetProperty("status").GetInt32(),
            actual.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            expected.RootElement.GetProperty("title").GetString(),
            actual.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            expected.RootElement.GetProperty("type").GetString(),
            actual.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            expected.RootElement.GetProperty("detail").GetString(),
            actual.RootElement.GetProperty("detail").GetString());
        Assert.Equal("/commands", actual.RootElement.GetProperty("instance").GetString());
        Assert.Equal(handlerContext.TraceIdentifier, actual.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_InvalidException_WritesSameProblemWithErrorsAsToHttpResult()
    {
        IReadOnlyList<ValidationFailure> failures = [new ValidationFailure("Amount must be positive", "Amount", "validation.amount.negative")];
        const string reason = "Amount: Amount must be positive";
        const string code = "validation.amount.negative";
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), NullLogger<LiteBusExceptionHandler>.Instance);

        var resultContext = CreateHttpContext();
        var result = MediationResult.Invalid(reason, code, failures);
        var resultResponse = result.ToHttpResult(resultContext, () => Results.Ok());
        using var expected = await ExecuteToProblemAsync(resultResponse, resultContext).ConfigureAwait(false);

        var handlerContext = CreateHttpContext();
        var exception = new LiteBusMessageInvalidException(typeof(object), failures);
        using var actual = await HandleToProblemAsync(handler, handlerContext, exception).ConfigureAwait(false);

        Assert.Equal(422, actual.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            expected.RootElement.GetProperty("title").GetString(),
            actual.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            expected.RootElement.GetProperty("type").GetString(),
            actual.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            expected.RootElement.GetProperty("detail").GetString(),
            actual.RootElement.GetProperty("detail").GetString());

        var expectedErrors = expected.RootElement.GetProperty("errors");
        var actualErrors = actual.RootElement.GetProperty("errors");
        Assert.Equal(
            expectedErrors.GetProperty("Amount")[0].GetString(),
            actualErrors.GetProperty("Amount")[0].GetString());
    }

    [Fact]
    public async Task TryHandleAsync_InvalidExceptionWithSeveralFailures_WritesErrorsWithFallbackMapping()
    {
        IReadOnlyList<ValidationFailure> failures =
        [
            new ValidationFailure("Amount must be positive", "Amount"),
            new ValidationFailure("The request is malformed")
        ];
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), NullLogger<LiteBusExceptionHandler>.Instance);
        var httpContext = CreateHttpContext();
        var exception = new LiteBusMessageInvalidException(typeof(object), failures);

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(400, httpContext.Response.StatusCode);
        Assert.Equal("Invalid request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:mediation:invalid", problem.RootElement.GetProperty("type").GetString());

        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal("Amount must be positive", errors.GetProperty("Amount")[0].GetString());
        Assert.Equal("The request is malformed", errors.GetProperty(string.Empty)[0].GetString());
    }

    [Fact]
    public async Task TryHandleAsync_OtherException_ReturnsFalseWithoutWriting()
    {
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), NullLogger<LiteBusExceptionHandler>.Instance);
        var httpContext = CreateHttpContext();
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var handled = await handler.TryHandleAsync(
            httpContext, new InvalidOperationException("boom"), CancellationToken.None).ConfigureAwait(false);

        Assert.False(handled);
        Assert.Equal(0L, body.Length);
    }

    [Fact]
    public async Task TryHandleAsync_WithProblemDetailsService_UsesRegisteredService()
    {
        var recording = new RecordingProblemDetailsService();
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), NullLogger<LiteBusExceptionHandler>.Instance);
        var httpContext = CreateHttpContext(problemDetailsService: recording);
        var exception = new LiteBusMessageDeniedException(typeof(object), "the message was refused", "authorization.forbidden");

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None).ConfigureAwait(false);

        Assert.True(handled);
        Assert.True(recording.WasCalled);
        var written = recording.Written;
        Assert.NotNull(written);
        Assert.Equal(403, written.Status);
        Assert.Equal("Request denied", written.Title);
    }

    [Fact]
    public async Task TryHandleAsync_WithDefaultProblemDetailsService_WritesProblem()
    {
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), NullLogger<LiteBusExceptionHandler>.Instance);
        var httpContext = CreateHttpContext(addProblemDetails: true);
        var exception = new LiteBusMessageDeniedException(typeof(object), "the message was refused", "authorization.forbidden");

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(403, httpContext.Response.StatusCode);
        Assert.Equal("Request denied", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:authorization:forbidden", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_WithoutProblemDetailsService_WritesProblemDirectly()
    {
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), NullLogger<LiteBusExceptionHandler>.Instance);
        var httpContext = CreateHttpContext();
        var exception = new LiteBusMessageDeniedException(typeof(object), "the message was refused", "authorization.forbidden");

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(403, httpContext.Response.StatusCode);
        Assert.Equal("Request denied", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:authorization:forbidden", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("the message was refused", problem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_UnmappedCodeWithUseFallback_WritesBadRequest()
    {
        var logger = new CapturingLogger();
        var handler = new LiteBusExceptionHandler(new LiteBusExceptionHandlerOptions(), logger);
        var httpContext = CreateHttpContext();
        var exception = new LiteBusMessageDeniedException(typeof(object), "the account is unpaid", "billing.unpaid");

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(400, httpContext.Response.StatusCode);
        Assert.Equal("Invalid request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:billing:unpaid", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("the account is unpaid", problem.RootElement.GetProperty("detail").GetString());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task TryHandleAsync_UnmappedCodeWithTreatAsServerError_WritesInternalServerErrorAndLogs()
    {
        var logger = new CapturingLogger();
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions { UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError }, logger);
        var httpContext = CreateHttpContext();
        var exception = new LiteBusMessageDeniedException(typeof(object), "the account is unpaid", "billing.unpaid");

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(500, httpContext.Response.StatusCode);
        Assert.Equal("Internal server error", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("about:blank", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("the account is unpaid", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal("billing.unpaid", problem.RootElement.GetProperty("code").GetString());

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("billing.unpaid", entry.Message);
        Assert.Same(exception, entry.Exception);
    }

    [Fact]
    public async Task TryHandleAsync_UnmappedInvalidCodeWithTreatAsServerError_KeepsErrors()
    {
        var logger = new CapturingLogger();
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions { UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError }, logger);
        var httpContext = CreateHttpContext();
        IReadOnlyList<ValidationFailure> failures = [new ValidationFailure("Amount must be positive", "Amount", "billing.unpaid")];
        var exception = new LiteBusMessageInvalidException(typeof(object), failures);

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(500, httpContext.Response.StatusCode);
        Assert.Equal("Internal server error", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("about:blank", problem.RootElement.GetProperty("type").GetString());

        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal("Amount must be positive", errors.GetProperty("Amount")[0].GetString());
        Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task TryHandleAsync_NullCodeWithTreatAsServerError_UsesFallback()
    {
        var logger = new CapturingLogger();
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions { UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError }, logger);
        var httpContext = CreateHttpContext();
        var exception = new LiteBusMessageDeniedException(typeof(object), "the message was refused", code: null);

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(400, httpContext.Response.StatusCode);
        Assert.Equal("Invalid request", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:mediation:denied", problem.RootElement.GetProperty("type").GetString());
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task TryHandleAsync_RequestServicesCatalog_IsUsedForMapping()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("billing.unpaid", 402, "Billing required");
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), NullLogger<LiteBusExceptionHandler>.Instance);
        var httpContext = CreateHttpContext(catalog: catalog);
        var exception = new LiteBusMessageDeniedException(typeof(object), "the account is unpaid", "billing.unpaid");

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(402, httpContext.Response.StatusCode);
        Assert.Equal("Billing required", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_MessageFaultException_WritesInternalServerErrorAndLogs()
    {
        var logger = new CapturingLogger();
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), logger);
        var httpContext = CreateHttpContext();
        var exception = new MessageFaultException(typeof(object), "tenancy.lookup-failed", "The tenant lookup failed.");

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(500, httpContext.Response.StatusCode);
        Assert.Equal("Internal server error", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:tenancy:lookup-failed", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("The tenant lookup failed.", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal("tenancy.lookup-failed", problem.RootElement.GetProperty("code").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("tenancy.lookup-failed", entry.Message);
        Assert.Same(exception, entry.Exception);
    }

    [Fact]
    public async Task TryHandleAsync_MessageFaultExceptionWithoutReason_WritesGenericDetail()
    {
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), NullLogger<LiteBusExceptionHandler>.Instance);
        var httpContext = CreateHttpContext();
        var exception = new MessageFaultException("A fault no caller should read.");

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(500, httpContext.Response.StatusCode);
        Assert.Equal("Internal server error", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:mediation:failed", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("The mediation failed with a fault.", problem.RootElement.GetProperty("detail").GetString());
        Assert.False(problem.RootElement.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task TryHandleAsync_RegisteredProblemOptions_OverrideHandlerOptions()
    {
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions(), NullLogger<LiteBusExceptionHandler>.Instance);
        var httpContext = CreateHttpContext(
            options: new RefusalProblemOptions { UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError });
        var exception = new LiteBusMessageDeniedException(typeof(object), "the account is unpaid", "billing.unpaid");

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(500, httpContext.Response.StatusCode);
        Assert.Equal("about:blank", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_RegisteredProblemOptionsUseFallback_OverridesHandlerTreatAsServerError()
    {
        var handler = new LiteBusExceptionHandler(
            new LiteBusExceptionHandlerOptions { UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError },
            NullLogger<LiteBusExceptionHandler>.Instance);
        var httpContext = CreateHttpContext(options: new RefusalProblemOptions());
        var exception = new LiteBusMessageDeniedException(typeof(object), "the account is unpaid", "billing.unpaid");

        using var problem = await HandleToProblemAsync(handler, httpContext, exception).ConfigureAwait(false);

        Assert.Equal(400, httpContext.Response.StatusCode);
        Assert.Equal("urn:litebus:billing:unpaid", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void AddLiteBusExceptionHandler_RegistersDefaultOptionsAndHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiteBusExceptionHandler();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<LiteBusExceptionHandlerOptions>();
        var handler = provider.GetRequiredService<IExceptionHandler>();

        Assert.Equal(UnmappedRefusalCode.UseFallback, options.UnmappedRefusalCode);
        Assert.IsType<LiteBusExceptionHandler>(handler);
    }

    [Fact]
    public void AddLiteBusExceptionHandler_HonoursConfiguredOption()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiteBusExceptionHandler(options => options.UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<LiteBusExceptionHandlerOptions>();

        Assert.Equal(UnmappedRefusalCode.TreatAsServerError, options.UnmappedRefusalCode);
    }

    [Fact]
    public void AddLiteBusExceptionHandler_SeedsProblemOptionsWithUnmappedPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLiteBusExceptionHandler(options => options.UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<RefusalProblemOptions>();

        Assert.Equal(UnmappedRefusalCode.TreatAsServerError, options.UnmappedRefusalCode);
    }

    [Fact]
    public void AddLiteBusExceptionHandler_KeepsExplicitProblemOptions()
    {
        var explicitOptions = new RefusalProblemOptions
        {
            TypeFormatter = new DocumentationUrlProblemTypeFormatter("https://errors.example.com/{code}"),
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(explicitOptions);
        services.AddLiteBusExceptionHandler(options => options.UnmappedRefusalCode = UnmappedRefusalCode.TreatAsServerError);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<RefusalProblemOptions>();

        Assert.Same(explicitOptions, options);
        Assert.Equal(UnmappedRefusalCode.UseFallback, options.UnmappedRefusalCode);
    }

    private static DefaultHttpContext CreateHttpContext(
        IRefusalProblemCatalog? catalog = null,
        IProblemDetailsService? problemDetailsService = null,
        bool addProblemDetails = false,
        RefusalProblemOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (catalog is not null)
        {
            services.AddSingleton(catalog);
        }

        if (problemDetailsService is not null)
        {
            services.AddSingleton(problemDetailsService);
        }

        if (options is not null)
        {
            services.AddSingleton(options);
        }

        if (addProblemDetails)
        {
            services.AddProblemDetails();
        }

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        httpContext.Request.Path = "/commands";

        return httpContext;
    }

    private static async Task<JsonDocument> HandleToProblemAsync(
        LiteBusExceptionHandler handler,
        DefaultHttpContext httpContext,
        Exception exception)
    {
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None).ConfigureAwait(false);

        Assert.True(handled);

        body.Seek(0, SeekOrigin.Begin);

        var document = await JsonDocument.ParseAsync(body).ConfigureAwait(false);

        Assert.Equal("application/problem+json", httpContext.Response.ContentType);

        return document;
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

    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public bool WasCalled { get; private set; }

        public ProblemDetails? Written { get; private set; }


        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            WasCalled = true;
            Written = context.ProblemDetails;
            context.HttpContext.Response.StatusCode = context.ProblemDetails.Status ?? 500;

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingLogger : ILogger<LiteBusExceptionHandler>
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
