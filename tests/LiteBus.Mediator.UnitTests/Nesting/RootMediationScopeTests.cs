using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.Nesting;

/// <summary>
///     Verifies the root mediation scope clears ambient context for its lifetime and restores it on dispose.
/// </summary>
[Collection("Sequential")]
public sealed class RootMediationScopeTests : LiteBusTestBase
{
    [Fact]
    public void Begin_clears_ambient_and_dispose_restores_it()
    {
        AmbientExecutionContext.ResetForTesting();
        var outer = new StubExecutionContext(typeof(string));

        using (AmbientExecutionContext.CreateScope(outer))
        {
            AmbientExecutionContext.Current.Should().BeSameAs(outer);

            using (RootMediationScope.Begin())
            {
                AmbientExecutionContext.HasCurrent.Should().BeFalse();
            }

            AmbientExecutionContext.Current.Should().BeSameAs(outer);
        }

        AmbientExecutionContext.HasCurrent.Should().BeFalse();
        AmbientExecutionContext.ResetForTesting();
    }

    [Fact]
    public void Begin_without_ambient_and_dispose_leaves_no_ambient()
    {
        AmbientExecutionContext.ResetForTesting();
        AmbientExecutionContext.HasCurrent.Should().BeFalse();

        using (RootMediationScope.Begin())
        {
            AmbientExecutionContext.HasCurrent.Should().BeFalse();
        }

        AmbientExecutionContext.HasCurrent.Should().BeFalse();
        AmbientExecutionContext.ResetForTesting();
    }

    [Fact]
    public void Dispose_restores_only_once()
    {
        AmbientExecutionContext.ResetForTesting();
        var outer = new StubExecutionContext(typeof(string));

        using (AmbientExecutionContext.CreateScope(outer))
        {
            var scope = RootMediationScope.Begin();
            AmbientExecutionContext.HasCurrent.Should().BeFalse();

            scope.Dispose();
            AmbientExecutionContext.Current.Should().BeSameAs(outer);

            scope.Dispose();
            AmbientExecutionContext.Current.Should().BeSameAs(outer);
        }

        AmbientExecutionContext.ResetForTesting();
    }

    [Fact]
    public async Task RunAsync_runs_work_without_ambient_and_restores_after()
    {
        AmbientExecutionContext.ResetForTesting();
        var outer = new StubExecutionContext(typeof(string));

        using (AmbientExecutionContext.CreateScope(outer))
        {
            var observed = false;

            await RootMediationScope.RunAsync(
                () =>
                {
                    observed = !AmbientExecutionContext.HasCurrent;
                    return Task.CompletedTask;
                },
                CancellationToken.None).ConfigureAwait(false);

            observed.Should().BeTrue();
            AmbientExecutionContext.Current.Should().BeSameAs(outer);
        }

        AmbientExecutionContext.ResetForTesting();
    }

    [Fact]
    public async Task RunAsync_restores_ambient_when_work_throws()
    {
        AmbientExecutionContext.ResetForTesting();
        var outer = new StubExecutionContext(typeof(string));

        using (AmbientExecutionContext.CreateScope(outer))
        {
            var attempt = async () =>
            {
                await RootMediationScope.RunAsync(
                    () => Task.FromException(new InvalidOperationException("work failed")),
                    CancellationToken.None).ConfigureAwait(false);
            };

            await attempt.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            AmbientExecutionContext.Current.Should().BeSameAs(outer);
        }

        AmbientExecutionContext.ResetForTesting();
    }

    [Fact]
    public async Task RunAsync_throws_when_work_is_null()
    {
        var attempt = async () =>
        {
            await RootMediationScope.RunAsync(null!, CancellationToken.None).ConfigureAwait(false);
        };

        await attempt.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task RunAsync_throws_when_token_is_canceled()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync().ConfigureAwait(false);

        var called = false;
        var attempt = async () =>
        {
            await RootMediationScope.RunAsync(
                () =>
                {
                    called = true;
                    return Task.CompletedTask;
                },
                canceled.Token).ConfigureAwait(false);
        };

        await attempt.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        called.Should().BeFalse();
    }
}
