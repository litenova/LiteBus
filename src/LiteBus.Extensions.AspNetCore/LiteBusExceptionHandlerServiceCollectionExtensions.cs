using System;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Registers the LiteBus mediation exception handler with the ASP.NET Core service collection.
/// </summary>
public static class LiteBusExceptionHandlerServiceCollectionExtensions
{
    /// <summary>
    ///     Adds <see cref="LiteBusExceptionHandler" /> as the <see cref="IExceptionHandler" /> that maps mediation
    ///     refusals to problem responses.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configure">An optional callback that configures exception handler options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         Registers the configured <see cref="LiteBusExceptionHandlerOptions" /> as a singleton and the handler
    ///         with <c>AddExceptionHandler</c>, so <c>UseExceptionHandler</c> routes mediation exceptions through the
    ///         refusal catalog. The host may also call <c>AddProblemDetails</c> to customize the problem writer; when
    ///         absent, the handler writes the same shape directly.
    ///     </para>
    ///     <code><![CDATA[
    /// services.AddLiteBusExceptionHandler();
    /// app.UseExceptionHandler();
    /// ]]></code>
    /// </remarks>
    public static IServiceCollection AddLiteBusExceptionHandler(
        this IServiceCollection services,
        Action<LiteBusExceptionHandlerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new LiteBusExceptionHandlerOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddExceptionHandler<LiteBusExceptionHandler>();

        return services;
    }
}
