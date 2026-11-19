using System.Text.Json;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.UnitTests.AspNetCore.Problems;

public sealed class MediationResultValidationErrorsTests
{
    [Fact]
    public async Task ToHttpResult_InvalidWithFieldFailures_WritesErrorsExtension()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Invalid(
            "Amount: Amount must be positive; Currency: Currency is required",
            "validation.failed",
            [
                new ValidationFailure("Amount must be positive", "Amount"),
                new ValidationFailure("Amount must be below the limit", "Amount"),
                new ValidationFailure("Currency is required", "Currency"),
                new ValidationFailure("The request is malformed")
            ]);

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(422, httpContext.Response.StatusCode);
        Assert.Equal("Request validation failed", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:validation:failed", problem.RootElement.GetProperty("type").GetString());

        var errors = problem.RootElement.GetProperty("errors");
        var amount = errors.GetProperty("Amount");
        Assert.Equal(2, amount.GetArrayLength());
        Assert.Equal("Amount must be positive", amount[0].GetString());
        Assert.Equal("Amount must be below the limit", amount[1].GetString());

        var currency = errors.GetProperty("Currency");
        Assert.Equal("Currency is required", currency[0].GetString());

        var messageLevel = errors.GetProperty(string.Empty);
        Assert.Equal("The request is malformed", messageLevel[0].GetString());
    }

    [Fact]
    public async Task ToHttpResult_InvalidWithoutFailures_OmitsErrorsExtension()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Invalid("the request failed", code: null, failures: []);

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(400, httpContext.Response.StatusCode);
        Assert.Equal("urn:litebus:mediation:invalid", problem.RootElement.GetProperty("type").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ToHttpResult_Denied_OmitsErrorsExtension()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Denied("the message was refused", "authorization.forbidden");

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(403, httpContext.Response.StatusCode);
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ToHttpResult_GenericInvalid_WritesErrorsExtension()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult<string>.Invalid(
            "Amount: Amount must be positive",
            "validation.amount.negative",
            [new ValidationFailure("Amount must be positive", "Amount")]);

        var response = result.ToHttpResult(httpContext, value => Results.Ok(value));
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(422, httpContext.Response.StatusCode);
        Assert.Equal("Request validation failed", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:validation:amount:negative", problem.RootElement.GetProperty("type").GetString());

        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal("Amount must be positive", errors.GetProperty("Amount")[0].GetString());
    }

    [Fact]
    public async Task ToHttpResult_InvalidWithCustomCatalog_KeepsCatalogStatusTitleAndType()
    {
        var catalog = RefusalProblemCatalog.CreateDefault();
        catalog.Map("validation.failed", 400, "Custom validation title");
        var httpContext = CreateHttpContext();
        var result = MediationResult.Invalid(
            "Amount: Amount must be positive",
            "validation.failed",
            [new ValidationFailure("Amount must be positive", "Amount")]);

        var response = result.ToHttpResult(httpContext, () => Results.Ok(), catalog);
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(400, httpContext.Response.StatusCode);
        Assert.Equal("Custom validation title", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("urn:litebus:validation:failed", problem.RootElement.GetProperty("type").GetString());

        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal("Amount must be positive", errors.GetProperty("Amount")[0].GetString());
    }

    [Fact]
    public async Task ToHttpResult_InvalidWithCodedFailures_WritesErrorCodesAlignedWithErrors()
    {
        var httpContext = CreateHttpContext();
        var result = MediationResult.Invalid(
            "Amount: Amount must be positive",
            "validation.amount.negative",
            [
                new ValidationFailure("Amount must be positive", "Amount", "validation.amount.negative"),
                new ValidationFailure("Amount must be below the limit", "Amount"),
                new ValidationFailure("The request is malformed", code: "validation.malformed")
            ]);

        var response = result.ToHttpResult(httpContext, () => Results.Ok());
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        var errors = problem.RootElement.GetProperty("errors");
        var codes = problem.RootElement.GetProperty("errorCodes");

        var amountErrors = errors.GetProperty("Amount");
        var amountCodes = codes.GetProperty("Amount");
        Assert.Equal(2, amountErrors.GetArrayLength());
        Assert.Equal(amountErrors.GetArrayLength(), amountCodes.GetArrayLength());
        Assert.Equal("validation.amount.negative", amountCodes[0].GetString());
        Assert.Equal(JsonValueKind.Null, amountCodes[1].ValueKind);

        var messageCodes = codes.GetProperty(string.Empty);
        Assert.Equal("validation.malformed", messageCodes[0].GetString());
    }

    [Fact]
    public async Task ToHttpResult_ArrayValidationWriter_WritesErrorsArrayWithDetailPointerAndCode()
    {
        var httpContext = CreateHttpContext();
        var options = new RefusalProblemOptions { ValidationErrors = ArrayValidationErrorsWriter.Default };
        var result = MediationResult.Invalid(
            "Amount: Amount must be positive",
            "validation.amount.negative",
            [
                new ValidationFailure("Amount must be positive", "Amount", "validation.amount.negative"),
                new ValidationFailure("The request is malformed")
            ]);

        var response = result.ToHttpResult(httpContext, () => Results.Ok(), options: options);
        using var problem = await ExecuteToProblemAsync(response, httpContext).ConfigureAwait(false);

        Assert.Equal(422, httpContext.Response.StatusCode);

        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        Assert.Equal(2, errors.GetArrayLength());

        Assert.Equal("Amount must be positive", errors[0].GetProperty("detail").GetString());
        Assert.Equal("/Amount", errors[0].GetProperty("pointer").GetString());
        Assert.Equal("validation.amount.negative", errors[0].GetProperty("code").GetString());

        Assert.Equal("The request is malformed", errors[1].GetProperty("detail").GetString());
        Assert.Equal(JsonValueKind.Null, errors[1].GetProperty("pointer").ValueKind);
        Assert.Equal(JsonValueKind.Null, errors[1].GetProperty("code").ValueKind);

        Assert.False(problem.RootElement.TryGetProperty("errorCodes", out _));
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
}
