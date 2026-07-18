using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Saga;
using LiteBus.Saga.Abstractions;
using LiteBus.Saga.InboxIntegration;

namespace LiteBus.Storage.UnitTests.Saga;

/// <summary>
///     Verifies saga scope re-attachment at the nested command mediation boundary.
/// </summary>
public sealed class SagaInboxCommandScopePreHandlerTests
{
    /// <summary>
    ///     Verifies command mediation outside inbox execution leaves the saga context inactive.
    /// </summary>
    [Fact]
    public async Task PreHandleAsync_OutsideInboxExecution_ShouldLeaveContextInactive()
    {
        var context = new SagaExecutionContext();
        var preHandler = new SagaInboxCommandScopePreHandler(context);

        await preHandler.PreHandleAsync(new TestCommand()).ConfigureAwait(false);

        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies inbox execution without a durable message identifier leaves the saga context inactive.
    /// </summary>
    [Fact]
    public async Task PreHandleAsync_WithoutMessageId_ShouldLeaveContextInactive()
    {
        var context = new SagaExecutionContext();
        var preHandler = new SagaInboxCommandScopePreHandler(context);
        var executionContext = new TestExecutionContext();
        executionContext.Items[InboxExecutionContextKeys.IsInboxExecution] = true;

        using (AmbientExecutionContext.CreateScope(executionContext))
        {
            await preHandler.PreHandleAsync(new TestCommand()).ConfigureAwait(false);
        }

        context.IsActive.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies the durable message identifier re-attaches the exact processor-owned saga scope.
    /// </summary>
    [Fact]
    public async Task PreHandleAsync_WithInboxMessageId_ShouldAttachMatchingScope()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var registry = new SagaStateTypeRegistry();
        registry.RegisterStateType<TestSagaState>("orders.commands.process");
        var context = new SagaExecutionContext();
        var hook = new SagaProcessorHook(new InMemorySagaStore(serializer), registry, serializer, context);
        var preHandler = new SagaInboxCommandScopePreHandler(context);
        var envelope = new TestProcessorEnvelope(
            Guid.NewGuid(),
            "orders.commands.process",
            "order-42",
            "tenant-a");

        await hook.BeforeDispatchAsync(envelope).ConfigureAwait(false);

        Task nestedDispatch;
        using (System.Threading.ExecutionContext.SuppressFlow())
        {
            nestedDispatch = Task.Run(async () =>
            {
                context.IsActive.Should().BeFalse();
                var executionContext = new TestExecutionContext();
                executionContext.Items[InboxExecutionContextKeys.IsInboxExecution] = true;
                executionContext.Items[InboxExecutionContextKeys.MessageId] = envelope.MessageId;

                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    await preHandler.PreHandleAsync(new TestCommand()).ConfigureAwait(false);
                    context.IsActive.Should().BeTrue();
                    context.Correlation.Should().Be(new SagaCorrelation
                    {
                        SagaDefinitionId = "orders.commands.process",
                        CorrelationId = "order-42",
                        TenantId = "tenant-a"
                    });
                }
            });
        }

        await nestedDispatch.ConfigureAwait(false);
        hook.AbandonDispatchScope(envelope);
    }

    private sealed record TestCommand : ICommand;

    private sealed class TestSagaState
    {
    }

    private sealed record TestProcessorEnvelope(
        Guid MessageId,
        string ContractName,
        string? CorrelationId,
        string? TenantId) : IProcessorEnvelope
    {
        public int ContractVersion => 1;

        public string? CausationId => null;
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        private readonly Dictionary<string, object> _items = [];

        public CancellationToken CancellationToken => CancellationToken.None;

        public IDictionary<string, object> Items => _items;

        public IReadOnlyCollection<string> Tags => [];

        public object? MessageResult { get; set; }

        public void Abort(object? messageResult = null)
        {
            MessageResult = messageResult;
        }
    }
}
