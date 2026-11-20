using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Engine;

namespace LiteBus.Auditing.UnitTests.Checkpoints;

/// <summary>
///     Verifies that the checkpoint background service publishes pending checkpoints, cuts due ones, and backs off
///     after a failed pass.
/// </summary>
public sealed class CheckpointPublishBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_publishes_and_cuts_until_stopped()
    {
        var lifecycle = new CountingLifecycle();
        var service = new CheckpointPublishBackgroundService(
            new FixedServiceProvider(lifecycle),
            options: new CheckpointPublishHostOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(10),
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                MaxRetryDelay = TimeSpan.FromMilliseconds(10),
            });
        using var stopping = new CancellationTokenSource();

        _ = lifecycle.PublishCalls.Task.ContinueWith(
            _ => stopping.Cancel(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await service.ExecuteAsync(stopping.Token).ConfigureAwait(false);

        lifecycle.PublishCount.Should().BeGreaterThanOrEqualTo(1);
        lifecycle.CutDueCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_retries_after_a_failed_pass()
    {
        var lifecycle = new FlakyLifecycle();
        var service = new CheckpointPublishBackgroundService(
            new FixedServiceProvider(lifecycle),
            options: new CheckpointPublishHostOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(10),
                InitialRetryDelay = TimeSpan.FromMilliseconds(10),
                MaxRetryDelay = TimeSpan.FromMilliseconds(10),
            });
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = lifecycle.SecondPass.Task.ContinueWith(
            _ => stopping.Cancel(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await service.ExecuteAsync(stopping.Token).ConfigureAwait(false);

        lifecycle.Attempts.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ExecuteAsync_exits_when_the_lifecycle_is_not_registered()
    {
        var service = new CheckpointPublishBackgroundService(new FixedServiceProvider(null));

        await service.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    ///     A service provider that resolves one lifecycle or nothing.
    /// </summary>
    private sealed class FixedServiceProvider : IServiceProvider
    {
        /// <summary>
        ///     The lifecycle to resolve, or <see langword="null" /> for none.
        /// </summary>
        private readonly ICheckpointLifecycle? _lifecycle;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedServiceProvider" /> class.
        /// </summary>
        /// <param name="lifecycle">The lifecycle to resolve, or <see langword="null" /> for none.</param>
        public FixedServiceProvider(ICheckpointLifecycle? lifecycle)
        {
            _lifecycle = lifecycle;
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(ICheckpointLifecycle) ? _lifecycle : null;
        }
    }

    /// <summary>
    ///     A lifecycle that counts its passes and signals the first publish.
    /// </summary>
    private sealed class CountingLifecycle : ICheckpointLifecycle
    {
        /// <summary>
        ///     Gets how many publish passes ran.
        /// </summary>
        public int PublishCount { get; private set; }

        /// <summary>
        ///     Gets how many due scans ran.
        /// </summary>
        public int CutDueCount { get; private set; }

        /// <summary>
        ///     Gets the signal completed by the first publish pass.
        /// </summary>
        public TaskCompletionSource PublishCalls { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public ValueTask<AuditCheckpoint> CutAsync(
            string segment,
            string? label,
            AuditCheckpointKind kind,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new AuditCheckpoint(
                Guid.NewGuid(),
                segment,
                0,
                0,
                "hash",
                "sha256-v2",
                kind,
                DateTimeOffset.UtcNow));
        }

        /// <inheritdoc />
        public ValueTask<int> PublishPendingAsync(CancellationToken cancellationToken)
        {
            PublishCount++;
            PublishCalls.TrySetResult();

            return ValueTask.FromResult(0);
        }

        /// <inheritdoc />
        public ValueTask CutDueAsync(CancellationToken cancellationToken)
        {
            CutDueCount++;

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     A lifecycle whose first publish fails and whose later passes succeed.
    /// </summary>
    private sealed class FlakyLifecycle : ICheckpointLifecycle
    {
        /// <summary>
        ///     Gets how many publish passes ran.
        /// </summary>
        public int Attempts { get; private set; }

        /// <summary>
        ///     Gets the signal completed by the second publish pass.
        /// </summary>
        public TaskCompletionSource SecondPass { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public ValueTask<AuditCheckpoint> CutAsync(
            string segment,
            string? label,
            AuditCheckpointKind kind,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new AuditCheckpoint(
                Guid.NewGuid(),
                segment,
                0,
                0,
                "hash",
                "sha256-v2",
                kind,
                DateTimeOffset.UtcNow));
        }

        /// <inheritdoc />
        public ValueTask<int> PublishPendingAsync(CancellationToken cancellationToken)
        {
            Attempts++;

            if (Attempts == 1)
            {
                throw new InvalidOperationException("The anchor store is unreachable.");
            }

            if (Attempts == 2)
            {
                SecondPass.TrySetResult();
            }

            return ValueTask.FromResult(0);
        }

        /// <inheritdoc />
        public ValueTask CutDueAsync(CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}
