using System;
using System.Threading.Tasks;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Represents a dependency scope created for one message dispatch operation.
/// </summary>
public interface IMessageDispatchScope : IDisposable, IAsyncDisposable
{
    /// <summary>
    ///     Gets the scoped service provider used to resolve dispatch dependencies.
    /// </summary>
    /// <value>The scoped provider valid for the lifetime of this scope.</value>
    IServiceProvider ServiceProvider { get; }
}
