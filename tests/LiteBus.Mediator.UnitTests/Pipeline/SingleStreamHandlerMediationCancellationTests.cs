using System.Runtime.CompilerServices;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
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

    [Fact]
    public async Task StreamAsync_WithEnumeratorCanceledDuringPreStage_ShouldCancelTheStage()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddQueries(builder =>
                {
                    builder.Register<GuardedStreamQuery>();
                    builder.Register<GuardedStreamQueryHandler>();
                    builder.Register<BlockingStreamQueryGuard>();
                });
            })
            .BuildServiceProvider();

        var query = new GuardedStreamQuery();
        var stream = provider.GetRequiredService<IQueryMediator>()
            .StreamAsync(query, cancellationToken: CancellationToken.None);

        using var cts = new CancellationTokenSource();

        var enumerating = Task.Run(async () =>
        {
            await foreach (var item in stream.WithCancellation(cts.Token).ConfigureAwait(false))
            {
            }
        }, CancellationToken.None);

        // The guard is waiting inside the pre stage when the enumerator token fires, so the stage must observe the
        // enumerator cancellation rather than the stale mediation-time token.
        await query.GuardReached.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        cts.Cancel();

        var act = () => enumerating;

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

    private sealed class GuardedStreamQuery : IStreamQuery<int>
    {
        public TaskCompletionSource GuardReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GuardedStreamQueryHandler : IStreamQueryHandler<GuardedStreamQuery, int>
    {
        public async IAsyncEnumerable<int> StreamAsync(
            GuardedStreamQuery query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return 1;
        }
    }

    private sealed class BlockingStreamQueryGuard : IQueryGuard<GuardedStreamQuery>
    {
        public async Task<Verdict> DecideAsync(
            GuardedStreamQuery message,
            CancellationToken cancellationToken = default)
        {
            message.GuardReached.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return Verdict.Allow;
        }
    }
}
