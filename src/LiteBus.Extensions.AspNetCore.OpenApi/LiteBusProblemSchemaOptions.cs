using System.Collections.Generic;

namespace LiteBus.Extensions.AspNetCore.OpenApi;

/// <summary>
///     Options for the LiteBus problem schema OpenAPI transformer.
/// </summary>
/// <remarks>
///     <para>
///         The transformer adds the <c>code</c>, <c>traceId</c> and <c>errors</c> members to every component schema
///         named in <see cref="ProblemSchemaNames" />. When <see cref="IncludeRefusalCodes" /> is set, each problem
///         response also carries the refusal codes the catalog answers at its status code.
///     </para>
/// </remarks>
public sealed class LiteBusProblemSchemaOptions
{
    /// <summary>
    ///     Gets the component schema names the transformer enriches with the LiteBus problem members.
    /// </summary>
    /// <value>The default names are <c>ProblemDetails</c> and <c>HttpValidationProblemDetails</c>.</value>
    public IList<string> ProblemSchemaNames { get; } = ["ProblemDetails", "HttpValidationProblemDetails"];

    /// <summary>
    ///     Gets or sets a value indicating whether problem responses carry the refusal codes answered at their
    ///     status code.
    /// </summary>
    /// <value>The default is <see langword="false" />.</value>
    public bool IncludeRefusalCodes { get; set; }

    /// <summary>
    ///     Gets or sets the catalog refusal codes are read from.
    /// </summary>
    /// <value>
    ///     The default is <see langword="null" />, which reads the catalog registered in application services, or
    ///     the default catalog when none is registered.
    /// </value>
    public IRefusalProblemCatalog? RefusalCatalog { get; set; }
}
