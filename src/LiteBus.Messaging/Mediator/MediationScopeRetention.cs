using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Mediator;

/// <summary>
///     Retains an ambient execution context scope until an asynchronous mediation result completes.
/// </summary>
internal static class MediationScopeRetention
{
    /// <summary>
    ///     Retains the supplied scope until the mediation result completes or is disposed.
    /// </summary>
    /// <typeparam name="TResult">The mediation result type.</typeparam>
    /// <param name="result">The mediation result returned by the strategy.</param>
    /// <param name="scope">The ambient scope to dispose when mediation completes.</param>
    /// <returns>The mediation result with scope retention attached when required.</returns>
    public static TResult RetainUntilPipelineCompletes<TResult>(TResult result, AmbientExecutionContext.ExecutionContextScope scope)
    {
        if (result is Task task)
        {
            _ = task.ContinueWith(
                _ => scope.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return result;
        }

        var asyncEnumerableType = GetAsyncEnumerableElementType(result);

        if (asyncEnumerableType is not null)
        {
            var wrapperType = typeof(ScopeRetainedAsyncEnumerable<>).MakeGenericType(asyncEnumerableType);
            var wrapped = Activator.CreateInstance(wrapperType, result, scope);

            return (TResult) wrapped!;
        }

        scope.Dispose();
        return result;
    }

    /// <summary>
    ///     Gets the element type when the result implements <see cref="IAsyncEnumerable{T}" />.
    /// </summary>
    /// <typeparam name="TResult">The mediation result type.</typeparam>
    /// <param name="result">The mediation result.</param>
    /// <returns>The element type when the result is an asynchronous enumerable; otherwise, <see langword="null" />.</returns>
    private static Type? GetAsyncEnumerableElementType<TResult>(TResult result)
    {
        if (result is null)
        {
            return null;
        }

        foreach (var candidate in result.GetType().GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
            {
                return candidate.GenericTypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>
    ///     Wraps an asynchronous enumerable and disposes the ambient scope when enumeration completes.
    /// </summary>
    /// <typeparam name="T">The element type streamed by the mediation result.</typeparam>
    private sealed class ScopeRetainedAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        /// <summary>
        ///     The source asynchronous enumerable returned by mediation.
        /// </summary>
        private readonly IAsyncEnumerable<T> _source;

        /// <summary>
        ///     The ambient scope disposed when enumeration completes.
        /// </summary>
        private readonly AmbientExecutionContext.ExecutionContextScope _scope;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScopeRetainedAsyncEnumerable{T}" /> class.
        /// </summary>
        /// <param name="source">The source asynchronous enumerable returned by mediation.</param>
        /// <param name="scope">The ambient scope to dispose when enumeration completes.</param>
        public ScopeRetainedAsyncEnumerable(IAsyncEnumerable<T> source, AmbientExecutionContext.ExecutionContextScope scope)
        {
            _source = source;
            _scope = scope;
        }

        /// <inheritdoc />
        public IAsyncEnumerator<T> GetAsyncEnumerator(System.Threading.CancellationToken cancellationToken = default)
        {
            return new ScopeRetainedAsyncEnumerator(_source.GetAsyncEnumerator(cancellationToken), _scope);
        }

        /// <summary>
        ///     Wraps an asynchronous enumerator and disposes the ambient scope when enumeration completes.
        /// </summary>
        private sealed class ScopeRetainedAsyncEnumerator : IAsyncEnumerator<T>
        {
            /// <summary>
            ///     The source asynchronous enumerator returned by mediation.
            /// </summary>
            private readonly IAsyncEnumerator<T> _source;

            /// <summary>
            ///     The ambient scope disposed when enumeration completes.
            /// </summary>
            private readonly AmbientExecutionContext.ExecutionContextScope _scope;

            /// <summary>
            ///     Indicates whether the ambient scope has already been disposed.
            /// </summary>
            private bool _scopeDisposed;

            /// <summary>
            ///     Initializes a new instance of the <see cref="ScopeRetainedAsyncEnumerator" /> class.
            /// </summary>
            /// <param name="source">The source asynchronous enumerator returned by mediation.</param>
            /// <param name="scope">The ambient scope to dispose when enumeration completes.</param>
            public ScopeRetainedAsyncEnumerator(IAsyncEnumerator<T> source, AmbientExecutionContext.ExecutionContextScope scope)
            {
                _source = source;
                _scope = scope;
            }

            /// <inheritdoc />
            public T Current => _source.Current;

            /// <inheritdoc />
            public async ValueTask<bool> MoveNextAsync()
            {
                var hasNext = await _source.MoveNextAsync().ConfigureAwait(false);

                if (!hasNext)
                {
                    await DisposeScopeAsync().ConfigureAwait(false);
                }

                return hasNext;
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                await _source.DisposeAsync().ConfigureAwait(false);
                await DisposeScopeAsync().ConfigureAwait(false);
            }

            /// <summary>
            ///     Disposes the ambient scope once.
            /// </summary>
            /// <returns>A value task representing the dispose operation.</returns>
            private ValueTask DisposeScopeAsync()
            {
                if (_scopeDisposed)
                {
                    return ValueTask.CompletedTask;
                }

                _scopeDisposed = true;
                _scope.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
