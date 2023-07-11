using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.MediationStrategies;

public static class AsyncEnumerable
{
    /// <summary>
    ///     Creates an <see cref="IAsyncEnumerable{T}" /> which yields no results, similar to
    ///     <see cref="Enumerable.Empty{TResult}" />.
    /// </summary>
    public static IAsyncEnumerable<T> Empty<T>()
    {
        return EmptyAsyncEnumerator<T>.Instance;
    }

    private sealed class EmptyAsyncEnumerator<T> : IAsyncEnumerator<T>, IAsyncEnumerable<T>
    {
        public static readonly EmptyAsyncEnumerator<T> Instance = new();

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return this;
        }

        public T Current => default;

        public ValueTask DisposeAsync()
        {
            return default;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            return new ValueTask<bool>(false);
        }
    }
}