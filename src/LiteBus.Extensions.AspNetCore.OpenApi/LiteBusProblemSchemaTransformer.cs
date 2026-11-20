using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace LiteBus.Extensions.AspNetCore.OpenApi;

/// <summary>
///     Adds the problem members LiteBus always sends to the problem schemas of an OpenAPI document.
/// </summary>
/// <remarks>
///     <para>
///         The generated <c>ProblemDetails</c> schema carries only <c>type</c>, <c>title</c>, <c>status</c>,
///         <c>detail</c> and <c>instance</c>, while LiteBus answers every refusal with a <c>code</c>, a
///         <c>traceId</c> and, for validation refusals, an <c>errors</c> member. The transformer adds those members
///         to every component schema named in <see cref="LiteBusProblemSchemaOptions.ProblemSchemaNames" />, so
///         generated clients type the members the service actually sends.
///     </para>
///     <para>
///         When <see cref="LiteBusProblemSchemaOptions.IncludeRefusalCodes" /> is set, each problem response also
///         carries an <c>x-litebus-refusal-codes</c> extension listing the refusal codes the catalog answers at that
///         status code, ordered by code. Only catalogs derived from <see cref="RefusalProblemCatalog" /> expose
///         their mappings; any other <see cref="IRefusalProblemCatalog" /> implementation leaves responses
///         unannotated.
///     </para>
///     <para>
///         Properties and extensions the document already defines are left untouched: enriching a document twice
///         changes nothing the second time.
///     </para>
/// </remarks>
public sealed class LiteBusProblemSchemaTransformer : IOpenApiDocumentTransformer
{
    /// <summary>
    ///     The response extension holding the refusal codes answered at one status code.
    /// </summary>
    private const string RefusalCodesExtensionName = "x-litebus-refusal-codes";

    /// <summary>
    ///     The problem member holding the machine-readable refusal code.
    /// </summary>
    private const string CodePropertyName = "code";

    /// <summary>
    ///     The problem member holding the trace identifier.
    /// </summary>
    private const string TraceIdPropertyName = "traceId";

    /// <summary>
    ///     The problem member holding the validation failures.
    /// </summary>
    private const string ErrorsPropertyName = "errors";

    /// <summary>
    ///     The media type prefix marking a response as a problem response.
    /// </summary>
    private const string ProblemMediaTypePrefix = "application/problem+";

    /// <summary>
    ///     Describes the <c>code</c> member in the enriched schema.
    /// </summary>
    private const string CodeDescription =
        "The machine-readable refusal code the decision supplied, when it supplied one.";

    /// <summary>
    ///     Describes the <c>traceId</c> member in the enriched schema.
    /// </summary>
    private const string TraceIdDescription = "The trace identifier of the request that produced the problem.";

    /// <summary>
    ///     Describes the <c>errors</c> member in the enriched schema.
    /// </summary>
    private const string ErrorsDescription =
        "The validation failures keyed by member, with message-level failures under an empty key, in the same shape as ValidationProblemDetails.Errors.";

    /// <summary>
    ///     The configured problem schema names and refusal code policy.
    /// </summary>
    private readonly LiteBusProblemSchemaOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusProblemSchemaTransformer" /> class.
    /// </summary>
    /// <param name="options">The configured problem schema names and refusal code policy.</param>
    public LiteBusProblemSchemaTransformer(IOptions<LiteBusProblemSchemaOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Value);

        _options = options.Value;
    }

    /// <inheritdoc />
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var catalog = _options.IncludeRefusalCodes ? ResolveCatalog(context) : null;

        TransformDocument(document, catalog);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Adds the LiteBus problem members, and optionally the per-status refusal codes, to a document.
    /// </summary>
    /// <param name="document">The OpenAPI document to enrich.</param>
    /// <param name="catalog">The catalog refusal codes are read from, or <see langword="null" /> to skip refusal codes.</param>
    /// <remarks>
    ///     <para>
    ///         Applies the same enrichment <see cref="TransformAsync" /> applies, without needing the document
    ///         transformer context, so design-time tooling and tests can enrich a document they built themselves.
    ///         Refusal codes are added only when <see cref="LiteBusProblemSchemaOptions.IncludeRefusalCodes" /> is
    ///         set and <paramref name="catalog" /> carries mappings.
    ///     </para>
    /// </remarks>
    public void TransformDocument(OpenApiDocument document, IRefusalProblemCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(document);

        AddProblemMembers(document);

        if (_options.IncludeRefusalCodes && catalog is not null)
        {
            AddRefusalCodes(document, catalog);
        }
    }

    /// <summary>
    ///     Resolves the catalog refusal codes are read from.
    /// </summary>
    /// <param name="context">The document transformer context carrying the application services.</param>
    /// <returns>The configured catalog, the catalog registered in application services, or the default catalog.</returns>
    private IRefusalProblemCatalog ResolveCatalog(OpenApiDocumentTransformerContext context)
    {
        if (_options.RefusalCatalog is not null)
        {
            return _options.RefusalCatalog;
        }

        var services = context.ApplicationServices;

        if (services is not null && services.GetService<IRefusalProblemCatalog>() is IRefusalProblemCatalog registered)
        {
            return registered;
        }

        return RefusalProblemCatalog.Default;
    }

    /// <summary>
    ///     Adds the <c>code</c>, <c>traceId</c> and <c>errors</c> members to the problem schemas of a document.
    /// </summary>
    /// <param name="document">The OpenAPI document to enrich.</param>
    private void AddProblemMembers(OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;

        if (schemas is null)
        {
            return;
        }

        foreach (var name in _options.ProblemSchemaNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!schemas.TryGetValue(name, out var candidate) || candidate is not OpenApiSchema schema)
            {
                continue;
            }

            schema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

            if (!schema.Properties.ContainsKey(CodePropertyName))
            {
                schema.Properties[CodePropertyName] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = CodeDescription,
                };
            }

            if (!schema.Properties.ContainsKey(TraceIdPropertyName))
            {
                schema.Properties[TraceIdPropertyName] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = TraceIdDescription,
                };
            }

            if (!schema.Properties.ContainsKey(ErrorsPropertyName))
            {
                schema.Properties[ErrorsPropertyName] = CreateErrorsSchema();
            }
        }
    }

    /// <summary>
    ///     Creates the schema of the <c>errors</c> member written by the default validation errors writer.
    /// </summary>
    /// <returns>The object schema holding one string array per member.</returns>
    private static OpenApiSchema CreateErrorsSchema()
    {
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = ErrorsDescription,
            AdditionalPropertiesAllowed = true,
            AdditionalProperties = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema { Type = JsonSchemaType.String },
            },
        };
    }

    /// <summary>
    ///     Annotates each problem response with the refusal codes answered at its status code.
    /// </summary>
    /// <param name="document">The OpenAPI document to enrich.</param>
    /// <param name="catalog">The catalog refusal codes are read from.</param>
    private static void AddRefusalCodes(OpenApiDocument document, IRefusalProblemCatalog catalog)
    {
        if (catalog is not RefusalProblemCatalog exportable)
        {
            return;
        }

        var codesByStatus = GroupCodesByStatus(exportable);

        if (codesByStatus.Count == 0 || document.Paths is null)
        {
            return;
        }

        foreach (var path in document.Paths.Values)
        {
            if (path?.Operations is null)
            {
                continue;
            }

            foreach (var operation in path.Operations.Values)
            {
                if (operation?.Responses is null)
                {
                    continue;
                }

                foreach (var (statusKey, response) in operation.Responses)
                {
                    AnnotateResponse(statusKey, response, codesByStatus);
                }
            }
        }
    }

    /// <summary>
    ///     Groups the refusal codes of a catalog export by status code.
    /// </summary>
    /// <param name="catalog">The catalog refusal codes are read from.</param>
    /// <returns>The refusal codes by status code, ordered by code within each status.</returns>
    private static Dictionary<int, List<string>> GroupCodesByStatus(RefusalProblemCatalog catalog)
    {
        var codesByStatus = new Dictionary<int, List<string>>();

        foreach (var mapping in catalog.Export().Mappings)
        {
            if (!codesByStatus.TryGetValue(mapping.StatusCode, out var codes))
            {
                codes = [];
                codesByStatus[mapping.StatusCode] = codes;
            }

            if (!codes.Contains(mapping.Pattern))
            {
                codes.Add(mapping.Pattern);
            }
        }

        foreach (var codes in codesByStatus.Values)
        {
            codes.Sort(StringComparer.Ordinal);
        }

        return codesByStatus;
    }

    /// <summary>
    ///     Annotates one response with the refusal codes answered at its status code, when it is a problem response.
    /// </summary>
    /// <param name="statusKey">The response key, holding the status code for status responses.</param>
    /// <param name="response">The response to annotate.</param>
    /// <param name="codesByStatus">The refusal codes by status code.</param>
    private static void AnnotateResponse(
        string statusKey,
        IOpenApiResponse response,
        Dictionary<int, List<string>> codesByStatus)
    {
        if (response is not OpenApiResponse problem
            || !int.TryParse(statusKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var statusCode)
            || !codesByStatus.TryGetValue(statusCode, out var codes)
            || !IsProblemResponse(problem))
        {
            return;
        }

        problem.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);

        var nodes = new List<JsonNode>(codes.Count);

        foreach (var code in codes)
        {
            nodes.Add(JsonValue.Create(code)!);
        }

        JsonNode[] items = [.. nodes];

        problem.Extensions[RefusalCodesExtensionName] = new JsonNodeExtension(new JsonArray(items));
    }

    /// <summary>
    ///     Determines whether a response carries problem content.
    /// </summary>
    /// <param name="response">The response to inspect.</param>
    /// <returns><see langword="true" /> when the response carries problem content; otherwise, <see langword="false" />.</returns>
    private static bool IsProblemResponse(OpenApiResponse response)
    {
        if (response.Content is null)
        {
            return false;
        }

        foreach (var contentType in response.Content.Keys)
        {
            if (contentType is not null
                && contentType.StartsWith(ProblemMediaTypePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
