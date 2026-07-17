using System;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Messaging.Mediator;

/// <summary>
///     Creates dispatch scopes that resolve handlers from the root provider when the host has no scope factory.
/// </summary>
internal sealed class RootMessageDispatchScopeFactory : IMessageDispatchScopeFactory
{
    /// <summary>
    ///     Gets the root service provider used when scoped host scopes are unavailable.
    /// </summary>
    private readonly IServiceProvider _rootServiceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RootMessageDispatchScopeFactory" /> class.
    /// </summary>
    /// <param name="rootServiceProvider">The root service provider supplied by the host container.</param>
    public RootMessageDispatchScopeFactory(IServiceProvider rootServiceProvider)
    {
        ArgumentNullException.ThrowIfNull(rootServiceProvider);
        _rootServiceProvider = rootServiceProvider;
    }

    /// <inheritdoc />
    public IMessageDispatchScope CreateScope()
    {
        return new RootMessageDispatchScope(_rootServiceProvider);
    }

    /// <summary>
    ///     Adapts the root provider to <see cref="IMessageDispatchScope" /> without disposing the host container.
    /// </summary>
    private sealed class RootMessageDispatchScope : IMessageDispatchScope
    {
        /// <summary>
        ///     Gets the root service provider used for handler resolution.
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RootMessageDispatchScope" /> class.
        /// </summary>
        /// <param name="serviceProvider">The root service provider.</param>
        public RootMessageDispatchScope(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public IServiceProvider ServiceProvider => _serviceProvider;

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
