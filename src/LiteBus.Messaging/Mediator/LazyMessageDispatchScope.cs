using System;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging.Mediator;

/// <summary>
///     Defers creation of a dispatch dependency scope until a handler is resolved.
/// </summary>
/// <remarks>
///     Stream mediation can return an asynchronous enumerable that the caller never enumerates. Deferring the host scope
///     keeps that abandoned result from retaining container-scoped services.
/// </remarks>
internal sealed class LazyMessageDispatchScope : IMessageDispatchScope
{
    /// <summary>
    ///     The factory used to create the underlying host scope.
    /// </summary>
    private readonly IMessageDispatchScopeFactory _scopeFactory;

    /// <summary>
    ///     Synchronizes creation and disposal of the underlying scope.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The provider facade that creates the host scope on first service resolution.
    /// </summary>
    private readonly IServiceProvider _deferredServiceProvider;

    /// <summary>
    ///     The underlying scope created on first service resolution.
    /// </summary>
    private IMessageDispatchScope? _scope;

    /// <summary>
    ///     Indicates whether this lazy scope has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LazyMessageDispatchScope" /> class.
    /// </summary>
    /// <param name="scopeFactory">The factory used to create the underlying host scope.</param>
    public LazyMessageDispatchScope(IMessageDispatchScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
        _deferredServiceProvider = new DeferredServiceProvider(this);
    }

    /// <inheritdoc />
    public IServiceProvider ServiceProvider => _deferredServiceProvider;

    /// <summary>
    ///     Gets the host provider, creating its scope when the first handler is resolved.
    /// </summary>
    /// <returns>The provider for the lazily created host scope.</returns>
    private IServiceProvider GetScopeProvider()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (_scope ??= _scopeFactory.CreateScope()).ServiceProvider;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        IMessageDispatchScope? scope;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            scope = _scope;
            _scope = null;
        }

        scope?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        IMessageDispatchScope? scope;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            scope = _scope;
            _scope = null;
        }

        if (scope is not null)
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Forwards service resolution to the host provider after creating its scope on demand.
    /// </summary>
    private sealed class DeferredServiceProvider : IServiceProvider
    {
        /// <summary>
        ///     The owning lazy dispatch scope.
        /// </summary>
        private readonly LazyMessageDispatchScope _owner;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeferredServiceProvider" /> class.
        /// </summary>
        /// <param name="owner">The lazy dispatch scope that owns the host provider.</param>
        public DeferredServiceProvider(LazyMessageDispatchScope owner)
        {
            _owner = owner;
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return _owner.GetScopeProvider().GetService(serviceType);
        }
    }
}
