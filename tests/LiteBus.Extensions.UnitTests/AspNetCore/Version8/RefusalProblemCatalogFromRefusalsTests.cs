using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Version8;

public sealed class RefusalProblemCatalogFromRefusalsTests
{
    public static TheoryData<RefusalKind, int> KindStatuses()
    {
        return new TheoryData<RefusalKind, int>
        {
            { RefusalKind.Denied, StatusCodes.Status403Forbidden },
            { RefusalKind.Invalid, StatusCodes.Status422UnprocessableEntity },
            { RefusalKind.Conflict, StatusCodes.Status409Conflict },
            { RefusalKind.NotFound, StatusCodes.Status404NotFound },
            { RefusalKind.Unavailable, StatusCodes.Status503ServiceUnavailable },
        };
    }

    [Theory]
    [MemberData(nameof(KindStatuses))]
    public void CreateFrom_MapsKindToStatus(RefusalKind kind, int statusCode)
    {
        var refusals = new StubRefusalCatalog(
            [new RefusalEntry("billing.unpaid", kind, "Billing refused", Retryable: false, Owner: "billing")]);

        var catalog = RefusalProblemCatalog.CreateFrom(refusals);
        var mapping = catalog.Resolve("billing.unpaid");

        Assert.Equal(statusCode, mapping.StatusCode);
        Assert.Equal("Billing refused", mapping.Title);
    }

    [Fact]
    public void CreateFrom_AnswersNotSignedInWithUnauthorized()
    {
        var refusals = new StubRefusalCatalog(
            [new RefusalEntry(AuthorizationPolicyRefusalCodes.NotSignedIn, RefusalKind.Denied, "Not signed in", Retryable: false, Owner: "LiteBus")]);

        var catalog = RefusalProblemCatalog.CreateFrom(refusals);
        var mapping = catalog.Resolve(AuthorizationPolicyRefusalCodes.NotSignedIn);

        Assert.Equal(StatusCodes.Status401Unauthorized, mapping.StatusCode);
        Assert.Equal("Not signed in", mapping.Title);
    }

    [Fact]
    public void CreateFrom_CarriesOwnerAsContributor()
    {
        var refusals = new StubRefusalCatalog(
            [new RefusalEntry("billing.unpaid", RefusalKind.Conflict, "Billing conflict", Retryable: true, Owner: "billing")]);

        var catalog = RefusalProblemCatalog.CreateFrom(refusals);

        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("billing.unpaid", entry.Pattern);
        Assert.Equal("billing", entry.Contributor);
    }

    [Fact]
    public void MapFrom_IdenticalNeutralEntries_AreNoOpOnDefaults()
    {
        var refusals = new StubRefusalCatalog(
            [
                new RefusalEntry("declaration.missing", RefusalKind.Denied, "Request denied", Retryable: false, Owner: "LiteBus"),
                new RefusalEntry(AuthorizationPolicyRefusalCodes.NotSignedIn, RefusalKind.Denied, "Not signed in", Retryable: false, Owner: "LiteBus"),
                new RefusalEntry("authorization.*", RefusalKind.Denied, "Request denied", Retryable: false, Owner: "LiteBus"),
                new RefusalEntry("tenancy.required", RefusalKind.Denied, "Request denied", Retryable: false, Owner: "LiteBus"),
                new RefusalEntry("validation.*", RefusalKind.Invalid, "Request validation failed", Retryable: false, Owner: "LiteBus"),
                new RefusalEntry("concurrency.conflict", RefusalKind.Conflict, "Concurrency conflict", Retryable: true, Owner: "LiteBus"),
            ]);

        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.MapFrom(refusals);

        Assert.Equal(8, catalog.Entries.Count);
        Assert.Equal("LiteBus", catalog.Entries.First(entry => entry.Pattern == "tenancy.required").Contributor);
    }

    [Fact]
    public void MapFrom_ConflictingNeutralEntry_ThrowsNamingBoth()
    {
        var refusals = new StubRefusalCatalog(
            [new RefusalEntry("tenancy.required", RefusalKind.Invalid, "Tenant sign-in required", Retryable: false, Owner: "Tenancy")]);
        var catalog = RefusalProblemCatalog.CreateDefault();

        var exception = Assert.Throws<LiteBusConfigurationException>(() => catalog.MapFrom(refusals));

        Assert.Contains("tenancy.required", exception.Message);
        Assert.Contains("LiteBus", exception.Message);
        Assert.Contains("Tenancy", exception.Message);
        Assert.Contains("Replace", exception.Message);
    }

    [Fact]
    public void AddRefusalProblemCatalog_ResolvesCatalogFromComposedRefusals()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRefusalCatalog>(new StubRefusalCatalog(
            [new RefusalEntry("billing.unpaid", RefusalKind.Conflict, "Billing conflict", Retryable: true, Owner: "billing")]));
        services.AddRefusalProblemCatalog();

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IRefusalProblemCatalog>();

        var mapping = catalog.Resolve("billing.unpaid");
        Assert.Equal(StatusCodes.Status409Conflict, mapping.StatusCode);
        Assert.Equal("Billing conflict", mapping.Title);
    }

    [Fact]
    public void AddRefusalProblemCatalog_ConfigureCallback_MapsExtraCodes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRefusalCatalog>(new StubRefusalCatalog([]));
        services.AddRefusalProblemCatalog(catalog =>
            catalog.Map("billing.*", StatusCodes.Status402PaymentRequired, "Billing required", "billing"));

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IRefusalProblemCatalog>();

        Assert.Equal(StatusCodes.Status402PaymentRequired, catalog.Resolve("billing.unpaid").StatusCode);
    }

    [Fact]
    public void AddRefusalProblemCatalog_ExistingRegistration_Wins()
    {
        var custom = RefusalProblemCatalog.CreateDefault();
        custom.Map("billing.unpaid", StatusCodes.Status402PaymentRequired, "Billing required");
        var services = new ServiceCollection();
        services.AddSingleton<IRefusalProblemCatalog>(custom);
        services.AddSingleton<IRefusalCatalog>(new StubRefusalCatalog([]));
        services.AddRefusalProblemCatalog();

        using var provider = services.BuildServiceProvider();

        Assert.Same(custom, provider.GetRequiredService<IRefusalProblemCatalog>());
    }

    [Fact]
    public void AddRefusalProblemCatalog_MissingComposition_ThrowsNamingAddMessaging()
    {
        var services = new ServiceCollection();
        services.AddRefusalProblemCatalog();

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IRefusalProblemCatalog>());

        Assert.Contains("AddMessaging", exception.Message);
    }

    [Fact]
    public async Task ToHttpResult_UnmappedDeniedCode_AnswersDefaultAndLogsWarning()
    {
        var logger = new CapturingLogger();
        var httpContext = CreateHttpContext(catalogLogger: logger);
        var result = MediationResult.Denied("the account is unpaid", "billing.unpaid");

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal("Invalid request", problem.RootElement.GetProperty("title").GetString());

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("billing.unpaid", entry.Message);
    }

    [Fact]
    public async Task ToHttpResult_MappedDeniedCode_LogsNothing()
    {
        var logger = new CapturingLogger();
        var httpContext = CreateHttpContext(catalogLogger: logger);
        var result = MediationResult.Denied("the message was refused", "authorization.forbidden");

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task ToHttpResult_UnmappedInvalidCode_AnswersDefaultAndLogsDebug()
    {
        var logger = new CapturingLogger();
        var httpContext = CreateHttpContext(catalogLogger: logger);
        var result = MediationResult.Invalid(
            "a client is required",
            "client-required",
            [new ValidationFailure("A client is required.", "ClientId", "client-required")]);

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Contains("client-required", entry.Message);
    }

    private static DefaultHttpContext CreateHttpContext(ILogger<RefusalProblemCatalog>? catalogLogger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

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
        using var body = new MemoryStream();
        httpContext.Response.Body = body;
        await result.ExecuteAsync(httpContext).ConfigureAwait(false);

        body.Seek(0, SeekOrigin.Begin);

        Assert.Equal("application/problem+json", httpContext.Response.ContentType);

        return await JsonDocument.ParseAsync(body).ConfigureAwait(false);
    }

    private sealed class StubRefusalCatalog : IRefusalCatalog
    {
        public StubRefusalCatalog(IReadOnlyList<RefusalEntry> entries)
        {
            Entries = entries;
        }

        public IReadOnlyList<RefusalEntry> Entries { get; }

        public IRefusalWriter Map(string code, RefusalKind kind, string title, bool retryable, string? owner = null)
        {
            throw new NotSupportedException("The stub catalog is read-only.");
        }

        public IRefusalWriter Replace(string code, RefusalKind kind, string title, bool retryable, string? owner = null)
        {
            throw new NotSupportedException("The stub catalog is read-only.");
        }

        public bool TryResolve(string? code, [NotNullWhen(true)] out RefusalEntry? entry)
        {
            entry = Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.Pattern, code, StringComparison.Ordinal));

            return entry is not null;
        }
    }

    private sealed class CapturingLogger : ILogger<RefusalProblemCatalog>
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
