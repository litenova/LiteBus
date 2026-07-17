using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Mediator;

/// <summary>
///     Retains an ambient execution context scope until an asynchronous mediation result completes.
/// </summary>
internal static class MediationScopeRetention
{
    /// <summary>
    ///     The generic task wrapper method closed for the result type returned by mediation.
    /// </summary>
    private static readonly MethodInfo RetainGenericTaskMethod = typeof(MediationScopeRetention)
        .GetMethod(nameof(RetainGenericTaskAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    ///     Retains the supplied scope until the mediation result completes or is disposed.
    /// </summary>
    /// <typeparam name="TResult">The mediation result type.</typeparam>
    /// <param name="result">The mediation result returned by the strategy.</param>
    /// <param name="resourceScope">The mediation resources to dispose when mediation completes.</param>
    /// <returns>The mediation result with scope retention attached when required.</returns>
    public static TResult RetainUntilPipelineCompletes<TResult>(TResult result, MediationResourceScope resourceScope)
    {
        if (result is Task task)
        {
            return (TResult)RetainTask(task, resourceScope);
        }

        var asyncEnumerableType = GetAsyncEnumerableElementType(result);

        if (asyncEnumerableType is not null)
        {
            var wrapperType = typeof(ScopeRetainedAsyncEnumerable<>).MakeGenericType(asyncEnumerableType);
            var wrapped = Activator.CreateInstance(wrapperType, result, resourceScope);

            return (TResult) wrapped!;
        }

        resourceScope.Dispose();
        return result;
    }

    /// <summary>
    ///     Wraps a task so asynchronous scope disposal completes before the returned task completes.
    /// </summary>
    /// <param name="task">The mediation task.</param>
    /// <param name="resourceScope">The resources retained for the mediation operation.</param>
    /// <returns>A task with the same result shape and retained resource lifetime.</returns>
    private static object RetainTask(Task task, MediationResourceScope resourceScope)
    {
        var resultType = GetTaskResultType(task.GetType());

        if (resultType is null)
        {
            return RetainTaskAsync(task, resourceScope);
        }

        return RetainGenericTaskMethod
            .MakeGenericMethod(resultType)
            .Invoke(null, [task, resourceScope])!;
    }

    /// <summary>
    ///     Gets the result type carried by a task implementation.
    /// </summary>
    /// <param name="taskType">The runtime task type.</param>
    /// <returns>The task result type, or <see langword="null" /> for a non-generic task.</returns>
    private static Type? GetTaskResultType(Type taskType)
    {
        for (var candidate = taskType; candidate is not null; candidate = candidate.BaseType)
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(Task<>))
            {
                return candidate.GenericTypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>
    ///     Awaits a non-generic mediation task and then disposes retained resources.
    /// </summary>
    /// <param name="task">The mediation task.</param>
    /// <param name="resourceScope">The retained mediation resources.</param>
    /// <returns>A task that includes resource disposal.</returns>
    private static async Task RetainTaskAsync(Task task, MediationResourceScope resourceScope)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            await resourceScope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Awaits a generic mediation task and then disposes retained resources.
    /// </summary>
    /// <typeparam name="T">The mediation task result type.</typeparam>
    /// <param name="task">The mediation task.</param>
    /// <param name="resourceScope">The retained mediation resources.</param>
    /// <returns>A task that carries the result and includes resource disposal.</returns>
    private static async Task<T> RetainGenericTaskAsync<T>(Task task, MediationResourceScope resourceScope)
    {
        try
        {
            return await ((Task<T>)task).ConfigureAwait(false);
        }
        finally
        {
            await resourceScope.DisposeAsync().ConfigureAwait(false);
        }
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
        ///     The mediation resources disposed when enumeration completes.
        /// </summary>
        private readonly MediationResourceScope _resourceScope;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScopeRetainedAsyncEnumerable{T}" /> class.
        /// </summary>
        /// <param name="source">The source asynchronous enumerable returned by mediation.</param>
        /// <param name="resourceScope">The mediation resources to dispose when enumeration completes.</param>
        public ScopeRetainedAsyncEnumerable(IAsyncEnumerable<T> source, MediationResourceScope resourceScope)
        {
            _source = source;
            _resourceScope = resourceScope;
        }

        /// <inheritdoc />
        public IAsyncEnumerator<T> GetAsyncEnumerator(System.Threading.CancellationToken cancellationToken = default)
        {
            return new ScopeRetainedAsyncEnumerator(_source.GetAsyncEnumerator(cancellationToken), _resourceScope);
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
            ///     The mediation resources disposed when enumeration completes.
            /// </summary>
            private readonly MediationResourceScope _resourceScope;

            /// <summary>
            ///     Indicates whether the mediation resources have already been disposed.
            /// </summary>
            private bool _resourceScopeDisposed;

            /// <summary>
            ///     Initializes a new instance of the <see cref="ScopeRetainedAsyncEnumerator" /> class.
            /// </summary>
            /// <param name="source">The source asynchronous enumerator returned by mediation.</param>
            /// <param name="resourceScope">The mediation resources to dispose when enumeration completes.</param>
            public ScopeRetainedAsyncEnumerator(IAsyncEnumerator<T> source, MediationResourceScope resourceScope)
            {
                _source = source;
                _resourceScope = resourceScope;
            }

            /// <inheritdoc />
            public T Current => _source.Current;

            /// <inheritdoc />
            public async ValueTask<bool> MoveNextAsync()
            {
                bool hasNext;

                try
                {
                    hasNext = await _source.MoveNextAsync().ConfigureAwait(false);
                }
                catch
                {
                    await DisposeScopeAsync().ConfigureAwait(false);
                    throw;
                }

                if (!hasNext)
                {
                    await DisposeScopeAsync().ConfigureAwait(false);
                }

                return hasNext;
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await _source.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await DisposeScopeAsync().ConfigureAwait(false);
                }
            }

            /// <summary>
            ///     Disposes the mediation resources once.
            /// </summary>
            /// <returns>A value task representing the dispose operation.</returns>
            private ValueTask DisposeScopeAsync()
            {
                if (_resourceScopeDisposed)
                {
                    return ValueTask.CompletedTask;
                }

                _resourceScopeDisposed = true;
                return _resourceScope.DisposeAsync();
            }
        }
    }
}
