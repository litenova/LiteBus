using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Messaging.Processing;
using LiteBus.Testing;

namespace LiteBus.MessageModule.UnitTests;

/// <summary>
///     Verifies lease heartbeat renewal and lease-loss cancellation behavior.
/// </summary>
public sealed class ProcessorLeaseHeartbeatTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Verifies dispatch completes when heartbeat is disabled.
    /// </summary>
    [Fact]
    public async Task RunWithHeartbeatAsync_when_interval_zero_should_run_operation_without_renewal()
    {
        var store = new RenewTrackingLeaseStore();
        var executed = false;

        var result = await ProcessorLeaseHeartbeat.RunWithHeartbeatAsync(
            new LeaseHeartbeatContext(
                Guid.NewGuid(),
                "worker",
                store,
                TimeSpan.FromMinutes(1),
                TimeSpan.Zero,
                new ManualTimeProvider(BaseTime),
                "Lease renewal failed."),
            _ =>
            {
                executed = true;
                return Task.FromResult(42);
            },
            CancellationToken.None);

        result.Should().Be(42);
        executed.Should().BeTrue();
        store.RenewCount.Should().Be(0);
    }

    /// <summary>
    ///     Verifies failed renewal cancels the linked dispatch token.
    /// </summary>
    [Fact]
    public async Task RunWithHeartbeatAsync_when_renewal_fails_should_cancel_operation()
    {
        var store = new FailingRenewLeaseStore();

        var act = () => ProcessorLeaseHeartbeat.RunWithHeartbeatAsync(
            new LeaseHeartbeatContext(
                Guid.NewGuid(),
                "worker",
                store,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromSeconds(15),
                new ManualTimeProvider(BaseTime),
                "Lease renewal failed."),
            token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(0);
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    ///     Lease store that always fails renewal.
    /// </summary>
    private sealed class FailingRenewLeaseStore : ILeaseRenewable
    {
        /// <inheritdoc />
        public Task<bool> RenewLeaseAsync(LeaseRenewalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    ///     Lease store that counts renewal attempts.
    /// </summary>
    private sealed class RenewTrackingLeaseStore : ILeaseRenewable
    {
        /// <summary>
        ///     Gets the number of renewal attempts observed by the test double.
        /// </summary>
        public int RenewCount { get; private set; }

        /// <inheritdoc />
        public Task<bool> RenewLeaseAsync(LeaseRenewalRequest request, CancellationToken cancellationToken = default)
        {
            RenewCount++;
            return Task.FromResult(true);
        }
    }
}
