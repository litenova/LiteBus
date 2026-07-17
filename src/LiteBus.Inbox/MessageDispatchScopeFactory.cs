using System;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox;

/// <summary>
///     Creates dependency injection scopes for inbox processor message dispatch.
/// </summary>
internal sealed class MessageDispatchScopeFactory : IMessageDispatchScopeFactory
{
    /// <summary>
    ///     Gets the underlying scope factory supplied by the host container.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageDispatchScopeFactory" /> class.
    /// </summary>
    /// <param name="scopeFactory">The scope factory supplied by the host container.</param>
    public MessageDispatchScopeFactory(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public IMessageDispatchScope CreateScope()
    {
        return new ServiceProviderMessageDispatchScope(_scopeFactory.CreateScope());
    }

    /// <summary>
    ///     Adapts a host <see cref="IServiceScope" /> to <see cref="IMessageDispatchScope" />.
    /// </summary>
    private sealed class ServiceProviderMessageDispatchScope : IMessageDispatchScope
    {
        /// <summary>
        ///     Gets the underlying host scope disposed with this adapter.
        /// </summary>
        private readonly IServiceScope _scope;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ServiceProviderMessageDispatchScope" /> class.
        /// </summary>
        /// <param name="scope">The host scope created for one dispatch operation.</param>
        public ServiceProviderMessageDispatchScope(IServiceScope scope)
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
