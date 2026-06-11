using System;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox;

/// <summary>
///     Creates dependency injection scopes for outbox processor message dispatch.
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
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
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
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        /// <inheritdoc />
        public IServiceProvider ServiceProvider => _scope.ServiceProvider;

        /// <inheritdoc />
        public void Dispose()
        {
            _scope.Dispose();
        }
    }
}