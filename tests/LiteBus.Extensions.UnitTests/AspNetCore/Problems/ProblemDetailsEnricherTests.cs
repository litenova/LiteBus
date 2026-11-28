using System.Text.Json;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Problems;

public sealed class ProblemDetailsEnricherTests
{
    [Fact]
    public void NullEnricher_LeavesProblemUnchanged()
    {
        var problem = new ProblemDetails
        {
            Status = 403,
            Title = "Request denied",
        };
        problem.Extensions["traceId"] = "trace-1";
        var httpContext = new DefaultHttpContext();
        var context = new ProblemDetailsEnrichmentContext(httpContext, MediationOutcome.Denied, "authorization.forbidden", "the message was refused");

        NullProblemDetailsEnricher.Default.Enrich(problem, context);

        Assert.Equal(403, problem.Status);
        Assert.Equal("Request denied", problem.Title);
        Assert.Single(problem.Extensions);
    }

    [Fact]
    public async Task CustomEnricher_AddsProjectionToResponse()
    {
        var httpContext = CreateHttpContext();
        var options = new RefusalProblemOptions { Enricher = new ErrorProjectionEnricher() };
        var result = MediationResult.Denied("the message was refused", "authorization.forbidden");

        var response = result.ToHttpResult(httpContext, () => Results.Ok(), options: options);
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(403, httpContext.Response.StatusCode);
        var error = problem.RootElement.GetProperty("error");
        Assert.Equal("authorization.forbidden", error.GetProperty("code").GetString());
        Assert.Equal("the message was refused", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CustomEnricher_ReplacesTitle()
    {
        var httpContext = CreateHttpContext();
        var options = new RefusalProblemOptions { Enricher = new LocalizingEnricher("Toegang geweigerd") };
        var result = MediationResult.Denied("the message was refused", "authorization.forbidden");

        var response = result.ToHttpResult(httpContext, () => Results.Ok(), options: options);
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal("Toegang geweigerd", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:authorization:forbidden", problem.RootElement.GetProperty("type").GetString());
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();

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

    private sealed class ErrorProjectionEnricher : IProblemDetailsEnricher
    {
        public void Enrich(ProblemDetails problem, ProblemDetailsEnrichmentContext context)
        {
            problem.Extensions["error"] = new Dictionary<string, string?>
            {
                ["code"] = context.Code,
                ["message"] = context.Reason,
            };
        }
    }

    private sealed class LocalizingEnricher : IProblemDetailsEnricher
    {
        private readonly string _title;

        public LocalizingEnricher(string title)
        {
            _title = title;
        }

        public void Enrich(ProblemDetails problem, ProblemDetailsEnrichmentContext context)
        {
            problem.Title = _title;
        }
    }
}
