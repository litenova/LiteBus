using System;
using Autofac;

namespace LiteBus.Runtime.Extensions.Autofac;

/// <summary>
///     Adapts <see cref="IComponentContext" /> to <see cref="IServiceProvider" /> for LiteBus factory registrations.
/// </summary>
internal sealed class AutofacServiceProviderAdapter : IServiceProvider
{
    /// <summary>
    ///     The Autofac lifetime scope used to resolve services after the original resolve operation completes.
    /// </summary>
    private readonly ILifetimeScope _lifetimeScope;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AutofacServiceProviderAdapter" /> class.
    /// </summary>
    /// <param name="lifetimeScope">The Autofac lifetime scope that resolves service instances.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lifetimeScope" /> is <see langword="null" />.</exception>
    public AutofacServiceProviderAdapter(ILifetimeScope lifetimeScope)
    {
        _lifetimeScope = lifetimeScope ?? throw new ArgumentNullException(nameof(lifetimeScope));
    }

    /// <summary>
    ///     Resolves a service instance from the Autofac component context.
    /// </summary>
    /// <param name="serviceType">The requested service type.</param>
    /// <returns>The resolved service instance, or <see langword="null" /> when Autofac cannot resolve the type.</returns>
    public object? GetService(Type serviceType)
    {
        return _lifetimeScope.ResolveOptional(serviceType);
    }
}
