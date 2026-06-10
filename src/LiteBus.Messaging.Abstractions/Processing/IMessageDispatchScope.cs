using System;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     A dependency injection scope created for one message dispatch operation.
/// </summary>
public interface IMessageDispatchScope : IDisposable
{
    /// <summary>
    ///     Gets the scoped service provider used to resolve handlers and dispatch dependencies.
    /// </summary>
    /// <value>The scoped provider valid for the lifetime of this scope.</value>
    IServiceProvider ServiceProvider { get; }
}
