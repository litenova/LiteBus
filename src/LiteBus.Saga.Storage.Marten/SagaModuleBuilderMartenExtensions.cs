using System;
using LiteBus.Saga;

namespace LiteBus.Saga.Storage.Marten;

/// <summary>
///     Provides Marten storage extensions for <see cref="SagaModuleBuilder" />.
/// </summary>
public static class SagaModuleBuilderMartenExtensions
{
    /// <summary>
    ///     Selects Marten saga storage for the current saga composition.
    /// </summary>
    /// <param name="builder">The saga module builder.</param>
    /// <param name="configure">The Marten saga storage configuration callback.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         The store resolves the application-owned <c>IDocumentStore</c> and the messaging
    ///         <c>TimeProvider</c>, and is registered as a singleton <c>ISagaStore</c>. The application
    ///         registers Marten itself with <c>AddMarten</c>; LiteBus does not register Marten.
    ///     </para>
    /// </remarks>
    public static SagaModuleBuilder UseMarten(
        this SagaModuleBuilder builder,
        Action<MartenSagaStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new MartenSagaStoreOptions();
        configure?.Invoke(options);

        return builder.RegisterStorage(new MartenSagaModule(options));
    }
}
