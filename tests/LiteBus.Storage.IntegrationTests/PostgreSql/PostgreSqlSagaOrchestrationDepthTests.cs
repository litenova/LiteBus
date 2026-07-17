using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Saga.Abstractions;
using LiteBus.Saga.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;
using static LiteBus.Storage.IntegrationTests.PostgreSql.SagaOrchestrationTestSupport;
using LiteBus.Storage.PostgreSql;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Integration tests for multi-step saga orchestration, compensation, and optimistic concurrency.
/// </summary>
public sealed class PostgreSqlSagaOrchestrationDepthTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const int ConcurrentWorkerCount = 4;

    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSagaOrchestrationDepthTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL fixture.</param>
    public PostgreSqlSagaOrchestrationDepthTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms two correlated inbox messages advance one saga instance through reserve and capture steps.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance()
    {
        var correlationId = $"order-{Guid.NewGuid():N}";
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        var sagaOptions = CreateSagaOptions();

        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions).ConfigureAwait(false);
        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, sagaOptions).ConfigureAwait(false);

         var provider = BuildSagaProvider(_fixture, inboxOptions, sagaOptions);
         await using (provider.ConfigureAwait(false))
         {
        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var sagaStore = provider.GetRequiredService<ISagaStore>();

        await inbox.AcceptAsync(InboxAcceptItem<OrderWorkflowSagaCommand>.From(
            new OrderWorkflowSagaCommand { Step = OrderWorkflowStep.ReserveInventory },
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated(correlationId) })).ConfigureAwait(false);

        await inbox.AcceptAsync(InboxAcceptItem<OrderWorkflowSagaCommand>.From(
            new OrderWorkflowSagaCommand { Step = OrderWorkflowStep.CapturePayment },
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated(correlationId) })).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);
        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var correlation = CreateCorrelation(correlationId);
        var instance = await sagaStore.LoadAsync<OrderWorkflowSagaState>(correlation).ConfigureAwait(false);

        instance.Should().NotBeNull();
        instance!.State.Step.Should().Be(2);
        instance.State.InventoryReserved.Should().BeTrue();
        instance.State.PaymentCaptured.Should().BeTrue();
        instance.IsCompleted.Should().BeFalse();
        instance.Version.Should().Be(2);

        var sagaRow = await PostgreSqlTableReaders.ReadSagaAsync(
            _fixture.DataSource,
            sagaOptions,
            correlationId,
            WorkflowContractName).ConfigureAwait(false);

        sagaRow.Should().NotBeNull();
        sagaRow!.OptimisticLockVersion.Should().Be(2);
        sagaRow.IsCompleted.Should().BeFalse();
        sagaRow.StateJson.Should().Contain("\"step\": 2");
        sagaRow.StateJson.Should().Contain("\"paymentCaptured\": true");

        var sagaRowCount = await PostgreSqlTableReaders.CountSagaRowsAsync(_fixture.DataSource, sagaOptions).ConfigureAwait(false);
        sagaRowCount.Should().Be(1);
        }
    }

    /// <summary>
    ///     Confirms a failed capture step does not persist partial state and compensation rolls back prior work.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_when_capture_fails_compensation_should_restore_prior_saga_state()
    {
        var correlationId = $"order-{Guid.NewGuid():N}";
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        var sagaOptions = CreateSagaOptions();

        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions).ConfigureAwait(false);
        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, sagaOptions).ConfigureAwait(false);

         var provider = BuildSagaProvider(_fixture, inboxOptions, sagaOptions);
         await using (provider.ConfigureAwait(false))
         {
        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var sagaStore = provider.GetRequiredService<ISagaStore>();
        var failureGate = provider.GetRequiredService<SagaStepFailureGate>();

        var reserveReceipt = await inbox.AcceptAsync(InboxAcceptItem<OrderWorkflowSagaCommand>.From(
            new OrderWorkflowSagaCommand { Step = OrderWorkflowStep.ReserveInventory },
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated(correlationId) })).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        failureGate.FailOn(OrderWorkflowStep.CapturePayment);

        var captureReceipt = await inbox.AcceptAsync(InboxAcceptItem<OrderWorkflowSagaCommand>.From(
            new OrderWorkflowSagaCommand { Step = OrderWorkflowStep.CapturePayment },
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated(correlationId) })).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var correlation = CreateCorrelation(correlationId);
        var afterFailure = await sagaStore.LoadAsync<OrderWorkflowSagaState>(correlation).ConfigureAwait(false);

        afterFailure.Should().NotBeNull();
        afterFailure!.State.Step.Should().Be(1);
        afterFailure.State.InventoryReserved.Should().BeTrue();
        afterFailure.State.PaymentCaptured.Should().BeFalse();
        afterFailure.State.Compensated.Should().BeFalse();
        afterFailure.IsCompleted.Should().BeFalse();
        afterFailure.Version.Should().Be(1);

        var captureRow = await PostgreSqlTableReaders.ReadInboxAsync(
            _fixture.DataSource,
            inboxOptions,
            captureReceipt.Id).ConfigureAwait(false);

        captureRow.Should().NotBeNull();
        captureRow!.Status.Should().Be(InboxStatus.Failed);
        captureRow.LastError.Should().Contain("Simulated failure");

        failureGate.Clear();

        await inbox.AcceptAsync(InboxAcceptItem<OrderWorkflowSagaCommand>.From(
            new OrderWorkflowSagaCommand { Step = OrderWorkflowStep.Compensate },
            InboxAcceptMetadata.Immediate with { Trace = new MessageTrace.Correlated(correlationId) })).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var afterCompensation = await sagaStore.LoadAsync<OrderWorkflowSagaState>(correlation).ConfigureAwait(false);

        afterCompensation.Should().NotBeNull();
        afterCompensation!.State.Step.Should().Be(1);
        afterCompensation.State.InventoryReserved.Should().BeFalse();
        afterCompensation.State.PaymentCaptured.Should().BeFalse();
        afterCompensation.State.Compensated.Should().BeTrue();
        afterCompensation.Version.Should().Be(2);

        var reserveRow = await PostgreSqlTableReaders.ReadInboxAsync(
            _fixture.DataSource,
            inboxOptions,
            reserveReceipt.Id).ConfigureAwait(false);

        reserveRow.Should().NotBeNull();
        reserveRow!.Status.Should().Be(InboxStatus.Completed);
        }
    }

    /// <summary>
    ///     Confirms parallel workers cannot lose saga updates when two increments race on one instance.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_parallel_workers_should_preserve_single_saga_version_increment()
    {
        var correlationId = $"order-{Guid.NewGuid():N}";
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        var sagaOptions = CreateSagaOptions();

        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions).ConfigureAwait(false);
        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, sagaOptions).ConfigureAwait(false);

         var provider = BuildSagaProvider(             _fixture,             inboxOptions,             sagaOptions,             processor => processor with             {                 BatchSize = 8,                 LeaseDuration = TimeSpan.FromMilliseconds(200),                 DispatcherConcurrency = 2,                 LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(50)             });
         await using (provider.ConfigureAwait(true))
         {

        var inbox = provider.GetRequiredService<IInbox>();
        var processingStore = provider.GetRequiredService<IInboxProcessingStore>();
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var processorOptions = provider.GetRequiredService<InboxProcessorOptions>();
        var clock = provider.GetRequiredService<TimeProvider>();
        var hooks = provider.GetServices<IProcessorEnvelopeHook>().ToArray();
        var sagaStore = provider.GetRequiredService<ISagaStore>();
        var delayGate = provider.GetRequiredService<SagaConcurrencyDelayGate>();
        delayGate.IncrementDelay = TimeSpan.FromMilliseconds(150);

        var firstMessageId = Guid.NewGuid();
        var secondMessageId = Guid.NewGuid();

        await inbox.AcceptAsync(InboxAcceptItem<OrderWorkflowSagaCommand>.From(
            new OrderWorkflowSagaCommand { Step = OrderWorkflowStep.Increment },
            InboxAcceptMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(firstMessageId),
                Trace = new MessageTrace.Correlated(correlationId)
            })).ConfigureAwait(false);

        await inbox.AcceptAsync(InboxAcceptItem<OrderWorkflowSagaCommand>.From(
            new OrderWorkflowSagaCommand { Step = OrderWorkflowStep.Increment },
            InboxAcceptMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(secondMessageId),
                Trace = new MessageTrace.Correlated(correlationId)
            })).ConfigureAwait(false);

        var processors = Enumerable.Range(0, ConcurrentWorkerCount)
            .Select(workerIndex => new PipelinedInboxProcessor(
                processingStore,
                processingStore,
                dispatcher,
                processorOptions with
                {
                    LeaseOwner = $"saga-depth-worker-{workerIndex}",
                    DispatcherConcurrency = 2,
                    LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(25)
                },
                clock,
                hooks))
            .ToArray();

        var workerTasks = processors.Select(processor => RunUntilIdleAsync(processor)).ToArray();
        await Task.WhenAll(workerTasks).ConfigureAwait(false);

        var correlation = CreateCorrelation(correlationId);
        var instance = await sagaStore.LoadAsync<OrderWorkflowSagaState>(correlation).ConfigureAwait(false);

        instance.Should().NotBeNull();
        instance!.State.Step.Should().BeInRange(1, 2);
        instance.Version.Should().BeGreaterThanOrEqualTo(1);
        instance.Version.Should().BeLessThanOrEqualTo(2);
        instance.IsCompleted.Should().BeFalse();

        var sagaRow = await PostgreSqlTableReaders.ReadSagaAsync(
            _fixture.DataSource,
            sagaOptions,
            correlationId,
            WorkflowContractName).ConfigureAwait(false);

        sagaRow.Should().NotBeNull();
        sagaRow!.OptimisticLockVersion.Should().Be(instance.Version);
        sagaRow.OptimisticLockVersion.Should().BeLessThanOrEqualTo(2);
        sagaRow.StateJson.Should().Contain($"\"step\": {instance.State.Step}");

        var sagaRowCount = await PostgreSqlTableReaders.CountSagaRowsAsync(_fixture.DataSource, sagaOptions).ConfigureAwait(false);
        sagaRowCount.Should().Be(1);

        var firstRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, inboxOptions, firstMessageId).ConfigureAwait(false);
        var secondRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, inboxOptions, secondMessageId).ConfigureAwait(false);

        firstRow.Should().NotBeNull();
        secondRow.Should().NotBeNull();
        firstRow!.Status.Should().NotBe(InboxStatus.Pending);
        secondRow!.Status.Should().NotBe(InboxStatus.Pending);

        var statuses = new[] { firstRow.Status, secondRow.Status };

        if (instance.State.Step == 1)
        {
            statuses.Count(status => status == InboxStatus.Completed).Should().Be(1);
            statuses.Should().Contain(status => status == InboxStatus.Failed || status == InboxStatus.DeadLettered);
        }
        else
        {
            statuses.Should().AllSatisfy(status => status.Should().Be(InboxStatus.Completed));
        }
        }
    }

    /// <summary>
    ///     Runs processor passes until no envelopes remain leased.
    /// </summary>
    /// <param name="processor">The processor to drain.</param>
    /// <returns>A task that completes when the queue is idle.</returns>
    private static async Task RunUntilIdleAsync(IInboxProcessor processor)
    {
        for (var pass = 0; pass < 50; pass++)
        {
            var result = await processor.ProcessPendingAsync().ConfigureAwait(false);

            if (result.LeasedCount == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException("Processor did not drain the queue within the pass limit.");
    }
}
