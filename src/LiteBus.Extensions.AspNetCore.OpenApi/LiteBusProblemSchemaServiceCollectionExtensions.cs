using System;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.AspNetCore.OpenApi;

/// <summary>
///     Registers the LiteBus problem schema OpenAPI transformer with the ASP.NET Core service collection.
/// </summary>
public static class LiteBusProblemSchemaServiceCollectionExtensions
{
    /// <summary>
    ///     Adds <see cref="LiteBusProblemSchemaTransformer" /> as the <see cref="IOpenApiDocumentTransformer" />
    ///     that adds the LiteBus problem members to the problem schemas of the OpenAPI document.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configure">An optional callback that configures problem schema options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         Registers the configured <see cref="LiteBusProblemSchemaOptions" /> and the transformer with
    ///         <c>AddOpenApi</c>, so <c>MapOpenApi</c> serves documents whose problem schemas carry the
    ///         <c>code</c>, <c>traceId</c> and <c>errors</c> members LiteBus always sends.
    ///     </para>
    ///     <code><![CDATA[
    /// services.AddLiteBusProblemSchema(schema =>
    /// {
    ///     schema.IncludeRefusalCodes = true;
    /// });
    /// app.MapOpenApi();
    /// ]]></code>
    /// </remarks>
    public static IServiceCollection AddLiteBusProblemSchema(
        this IServiceCollection services,
        Action<LiteBusProblemSchemaOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddOpenApi(openApi => openApi.AddDocumentTransformer<LiteBusProblemSchemaTransformer>());

        return services;
    }
}
