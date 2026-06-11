using LiteBus.Extensions.AspNetCore;

namespace LiteBus.Samples.V6;

/// <summary>
///     Copy-paste reference for production ASP.NET Core host setup. Not invoked by the local sample host.
/// </summary>
public static class ProductionHostTemplate
{
    /// <summary>
    ///     Registers authentication, authorization, and production-safe LiteBus management defaults.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    public static void ConfigureProductionManagement(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // builder.Services.AddAuthentication().AddJwtBearer(...);
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("LiteBusOperator", policy => policy.RequireRole("operator"));
        });

        builder.Services.AddLiteBusManagement(options =>
        {
            options.AuthorizationPolicy = "LiteBusOperator";
            options.FailHealthWhenNoProbes = true;
        });
    }
}