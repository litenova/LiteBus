using System;
using System.Threading.Tasks;
using Autofac;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.Extensions.Autofac;

/// <summary>
///     Creates dispatch scopes through Autofac lifetime scopes.
/// </summary>
internal sealed class AutofacMessageDispatchScopeFactory : IMessageDispatchScopeFactory
{
    /// <summary>
    ///     The root Autofac lifetime scope used to begin dispatch scopes.
    /// </summary>
    private readonly ILifetimeScope _rootScope;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AutofacMessageDispatchScopeFactory" /> class.
    /// </summary>
    /// <param name="rootScope">The root Autofac lifetime scope.</param>
    public AutofacMessageDispatchScopeFactory(ILifetimeScope rootScope)
    {
        ArgumentNullException.ThrowIfNull(rootScope);
        _rootScope = rootScope;
    }

    /// <inheritdoc />
    public IMessageDispatchScope CreateScope()
    {
        return new AutofacMessageDispatchScope(_rootScope.BeginLifetimeScope());
    }

    /// <summary>
    ///     Adapts an Autofac lifetime scope to the LiteBus contract.
    /// </summary>
    private sealed class AutofacMessageDispatchScope : IMessageDispatchScope
    {
        /// <summary>
        ///     The Autofac lifetime scope owned by this adapter.
        /// </summary>
        private readonly ILifetimeScope _scope;

        /// <summary>
        ///     The service provider view over the owned Autofac scope.
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AutofacMessageDispatchScope" /> class.
        /// </summary>
        /// <param name="scope">The lifetime scope created for one dispatch.</param>
        public AutofacMessageDispatchScope(ILifetimeScope scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            _scope = scope;
            _serviceProvider = new AutofacServiceProviderAdapter(scope);
        }

        /// <inheritdoc />
        public IServiceProvider ServiceProvider => _serviceProvider;

        /// <inheritdoc />
        public void Dispose()
        {
            _scope.Dispose();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _scope.DisposeAsync();
        }
    }
}
