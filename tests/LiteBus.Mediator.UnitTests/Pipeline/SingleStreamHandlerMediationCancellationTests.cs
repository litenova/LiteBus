using System.Runtime.CompilerServices;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Proves the stream strategy honours the enumerator cancellation token.
/// </summary>
public sealed class SingleStreamHandlerMediationCancellationTests
{
    [Fact]
    public async Task StreamAsync_WithCancellationAfterNone_ShouldSurfaceCancellationFromMoveNextAsync()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddQueries(builder =>
                {
                    builder.Register<BlockingStreamQuery>();
                    builder.Register<BlockingStreamQueryHandler>();
                });
            })
            .BuildServiceProvider();

        var stream = provider.GetRequiredService<IQueryMediator>()
            .StreamAsync(new BlockingStreamQuery(), cancellationToken: CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = async () =>
        {
            await foreach (var item in stream.WithCancellation(cts.Token).ConfigureAwait(false))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    private sealed class BlockingStreamQuery : IStreamQuery<int>
    {
    }

    private sealed class BlockingStreamQueryHandler : IStreamQueryHandler<BlockingStreamQuery, int>
    {
        public async IAsyncEnumerable<int> StreamAsync(
            BlockingStreamQuery query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            yield return 1;
        }
    }
}
