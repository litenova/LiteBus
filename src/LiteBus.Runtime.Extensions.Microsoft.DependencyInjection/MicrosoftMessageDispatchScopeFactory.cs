using System;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.Extensions.Microsoft.DependencyInjection;

/// <summary>
///     Creates dispatch scopes through Microsoft dependency injection.
/// </summary>
internal sealed class MicrosoftMessageDispatchScopeFactory : IMessageDispatchScopeFactory
{
    /// <summary>
    ///     The Microsoft dependency injection scope factory.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MicrosoftMessageDispatchScopeFactory" /> class.
    /// </summary>
    /// <param name="scopeFactory">The host scope factory.</param>
    public MicrosoftMessageDispatchScopeFactory(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public IMessageDispatchScope CreateScope()
    {
        return new MicrosoftMessageDispatchScope(_scopeFactory.CreateScope());
    }

    /// <summary>
    ///     Adapts a Microsoft dependency injection scope to the LiteBus contract.
    /// </summary>
    private sealed class MicrosoftMessageDispatchScope : IMessageDispatchScope
    {
        /// <summary>
        ///     The host scope owned by this adapter.
        /// </summary>
        private readonly IServiceScope _scope;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MicrosoftMessageDispatchScope" /> class.
        /// </summary>
        /// <param name="scope">The host scope created for one dispatch.</param>
        public MicrosoftMessageDispatchScope(IServiceScope scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            _scope = scope;
        }

        /// <inheritdoc />
        public IServiceProvider ServiceProvider => _scope.ServiceProvider;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_scope is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return;
            }

            _scope.Dispose();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (_scope is IAsyncDisposable asyncDisposable)
            {
                return asyncDisposable.DisposeAsync();
            }

            _scope.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
