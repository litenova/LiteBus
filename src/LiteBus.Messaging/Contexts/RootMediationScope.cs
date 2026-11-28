using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Clears the ambient execution context for the lifetime of the scope, so library-started work runs as a root mediation.
/// </summary>
/// <remarks>
///     <para>
///         A root mediation never inherits ambient context. It carries <see cref="MediationCause" /> instead, while
///         <see cref="IExecutionContext.Parent" /> stays <see langword="null" />. Boundaries such as the outbox processor,
///         immediate dispatch, inbox delivery, a recurring pass, or a saga timeout begin this scope before mediating, so
///         processor delivery and immediate delivery observe identical lineage.
///     </para>
///     <para>
///         Disposing the scope restores the execution context that was current when the scope began.
///     </para>
/// </remarks>
public sealed class RootMediationScope : IDisposable
{
    /// <summary>
    ///     The execution context that was current before this scope began.
    /// </summary>
    private readonly IExecutionContext? _previousContext;

    /// <summary>
    ///     Indicates whether this scope has already been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RootMediationScope" /> class.
    /// </summary>
    /// <param name="previousContext">The execution context that was current before this scope began.</param>
    private RootMediationScope(IExecutionContext? previousContext)
    {
        _previousContext = previousContext;
    }

    /// <summary>
    ///     Begins a root mediation scope that clears the ambient execution context until disposed.
    /// </summary>
    /// <returns>The scope that restores the previous context when disposed.</returns>
    public static RootMediationScope Begin()
    {
        IExecutionContext? previous = AmbientExecutionContext.TakeCurrent();
        return new RootMediationScope(previous);
    }

    /// <summary>
    ///     Runs the work as a root mediation without inheriting ambient context.
    /// </summary>
    /// <remarks>
    ///     The ambient execution context is cleared for the work and restored afterwards. Other ambient state, such as
    ///     the current <see cref="System.Diagnostics.Activity" />, still flows, so traces stay connected. A caller that
    ///     starts the work in the background passes this method to <see cref="Task.Run(Func{Task}, CancellationToken)" />.
    /// </remarks>
    /// <param name="work">The work to run without ambient context.</param>
    /// <param name="cancellationToken">The token used to cancel starting the work.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task RunAsync(Func<Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();

        using (Begin())
        {
            await work().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Disposes the scope and restores the previous execution context.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        AmbientExecutionContext.Restore(_previousContext);
        _disposed = true;
    }
}
