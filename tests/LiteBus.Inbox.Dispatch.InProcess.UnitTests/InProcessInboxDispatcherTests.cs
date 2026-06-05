using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Dispatch.InProcess.UnitTests;

[Collection("Sequential")]
public sealed class InProcessInboxDispatcherTests : LiteBusTestBase
{
    [Fact]
    public async Task DispatchAsync_ShouldExecuteCommandThroughMediator()
    {
        var recorder = new CommandRecorder();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ProcessOrderCommand>("orders.commands.process", 1);

        var serializer = new SystemTextJsonMessageSerializer();
        var payload = await serializer.SerializeAsync(new ProcessOrderCommand { OrderId = Guid.NewGuid() });

        await using var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder => builder.Register<ProcessOrderCommandHandler>());
                modules.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<ProcessOrderCommand>("orders.commands.process", 1);
                    builder.UseInProcessDispatcher();
                });
            })
            .BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var envelope = CreateEnvelope(
            contractRegistry,
            "orders.commands.process",
            1,
            payload,
            DateTimeOffset.UtcNow);

        await dispatcher.DispatchAsync(envelope);

        recorder.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchAsync_WhenMessageIsNotACommand_ShouldThrowInvalidOperationException()
    {
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<NonCommandPayload>("inbox.payload.non-command", 1);

        var serializer = new SystemTextJsonMessageSerializer();
        var payload = await serializer.SerializeAsync(new NonCommandPayload { Value = "not-a-command" });
        var envelope = CreateEnvelope(
            contractRegistry,
            "inbox.payload.non-command",
            1,
            payload,
            DateTimeOffset.UtcNow);

        var dispatcher = new InProcessInboxDispatcher(
            new NoOpCommandMediator(),
            contractRegistry,
            serializer);

        var act = () => dispatcher.DispatchAsync(envelope);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not implement ICommand*");
    }

    [Fact]
    public async Task DispatchAsync_ShouldSetIsInboxExecutionAndCopyTraceMetadata()
    {
        var inboxCapture = new IsInboxCapture();
        var traceCapture = new TraceMetadataCapture();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxProbeCommand>("inbox.commands.probe", 1);

        var serializer = new SystemTextJsonMessageSerializer();
        var payload = await serializer.SerializeAsync(new InboxProbeCommand());
        var envelope = CreateEnvelope(
            contractRegistry,
            "inbox.commands.probe",
            1,
            payload,
            DateTimeOffset.UtcNow,
            correlationId: "correlation-42",
            causationId: "causation-7",
            tenantId: "tenant-west");

        await using var provider = new ServiceCollection()
            .AddSingleton(inboxCapture)
            .AddSingleton(traceCapture)
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxProbeCommandHandler>();
                });

                modules.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxProbeCommand>("inbox.commands.probe", 1);
                    builder.UseInProcessDispatcher();
                });
            })
            .BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        await dispatcher.DispatchAsync(envelope);

        inboxCapture.IsInboxExecution.Should().BeTrue();
        traceCapture.CorrelationId.Should().Be("correlation-42");
        traceCapture.CausationId.Should().Be("causation-7");
        traceCapture.TenantId.Should().Be("tenant-west");
    }

    [Fact]
    public async Task DispatchAsync_WhenCancellationRequested_ShouldPassCancelledTokenToMediator()
    {
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxProbeCommand>("inbox.commands.probe", 1);

        var serializer = new SystemTextJsonMessageSerializer();
        var payload = await serializer.SerializeAsync(new InboxProbeCommand());
        var envelope = CreateEnvelope(
            contractRegistry,
            "inbox.commands.probe",
            1,
            payload,
            DateTimeOffset.UtcNow);

        var recordingSerializer = new CancellationRecordingSerializer(serializer);
        var mediator = new CancellationRecordingMediator();
        var dispatcher = new InProcessInboxDispatcher(mediator, contractRegistry, recordingSerializer);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            dispatcher.DispatchAsync(envelope, cts.Token));

        recordingSerializer.LastCancellationToken.IsCancellationRequested.Should().BeTrue();
        mediator.WasSendCalled.Should().BeFalse();
    }

    [Fact]
    public void AddInboxInProcessDispatcher_ShouldRegisterInProcessInboxDispatcher()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(_ => { });
                modules.AddInboxModule(inbox => inbox.UseInProcessDispatcher());
            })
            .BuildServiceProvider();

        serviceProvider.GetRequiredService<IInboxDispatcher>().Should().BeOfType<InProcessInboxDispatcher>();
    }

    [Fact]
    public void AddInboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(_ => { });
                modules.AddInboxModule();
                modules.Register(new PreRegisteredInboxDispatcherModule());
                modules.Register(new InProcessInboxDispatchModule());
            })
            .BuildServiceProvider();

        act.Should().Throw<LiteBus.Runtime.Abstractions.Exceptions.LiteBusConfigurationException>()
            .WithMessage("*IInboxDispatcher*");
    }

    private sealed class PreRegisteredInboxDispatcherModule : LiteBus.Runtime.Abstractions.IModule
    {
        public void Build(LiteBus.Runtime.Abstractions.IModuleConfiguration configuration)
        {
            configuration.DependencyRegistry.Register(new LiteBus.Runtime.Abstractions.DependencyDescriptor(
                typeof(IInboxDispatcher),
                typeof(StubInboxDispatcher)));
        }
    }

    private sealed class StubInboxDispatcher : IInboxDispatcher
    {
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void AddInboxInProcessDispatcher_WhenCalledTwice_ShouldThrow()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(_ => { });
                modules.AddInboxModule(inbox =>
                {
                    inbox.UseInProcessDispatcher();
                    inbox.UseInProcessDispatcher();
                });
            })
            .BuildServiceProvider();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Inbox dispatcher is already configured*");
    }

    private static InboxEnvelope CreateEnvelope(
        IMessageContractRegistry contractRegistry,
        string contractName,
        int contractVersion,
        string payload,
        DateTimeOffset createdAt,
        string? correlationId = null,
        string? causationId = null,
        string? tenantId = null)
    {
        return new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = contractName,
            ContractVersion = contractVersion,
            Payload = payload,
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            CorrelationId = correlationId,
            CausationId = causationId,
            TenantId = tenantId
        };
    }

    public sealed record ProcessOrderCommand : ICommand
    {
        public Guid OrderId { get; init; }
    }

    public sealed class ProcessOrderCommandHandler : ICommandHandler<ProcessOrderCommand>
    {
        private readonly CommandRecorder _recorder;

        public ProcessOrderCommandHandler(CommandRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(ProcessOrderCommand message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message);
            return Task.CompletedTask;
        }
    }

    public sealed class CommandRecorder
    {
        private readonly List<ProcessOrderCommand> _commands = [];

        public IReadOnlyList<ProcessOrderCommand> Commands => _commands;

        public void Record(ProcessOrderCommand command)
        {
            _commands.Add(command);
        }
    }

    public sealed record NonCommandPayload
    {
        public required string Value { get; init; }
    }

    public sealed record InboxProbeCommand : ICommand;

    public sealed class InboxProbeCommandHandler : ICommandHandler<InboxProbeCommand>
    {
        private readonly IsInboxCapture _inboxCapture;
        private readonly TraceMetadataCapture _traceCapture;

        public InboxProbeCommandHandler(IsInboxCapture inboxCapture, TraceMetadataCapture traceCapture)
        {
            _inboxCapture = inboxCapture;
            _traceCapture = traceCapture;
        }

        public Task HandleAsync(InboxProbeCommand message, CancellationToken cancellationToken = default)
        {
            _inboxCapture.IsInboxExecution =
                AmbientExecutionContext.Current.Items.TryGetValue(
                    InboxExecutionContextKeys.IsInboxExecution,
                    out var value) &&
                value is true;

            var items = AmbientExecutionContext.Current.Items;
            _traceCapture.CorrelationId = items.TryGetValue(MessageTraceContextKeys.CorrelationId, out var correlation)
                ? correlation as string
                : null;
            _traceCapture.CausationId = items.TryGetValue(MessageTraceContextKeys.CausationId, out var causation)
                ? causation as string
                : null;
            _traceCapture.TenantId = items.TryGetValue(MessageTraceContextKeys.TenantId, out var tenant)
                ? tenant as string
                : null;

            return Task.CompletedTask;
        }
    }

    public sealed class IsInboxCapture
    {
        public bool IsInboxExecution { get; set; }
    }

    public sealed class TraceMetadataCapture
    {
        public string? CorrelationId { get; set; }

        public string? CausationId { get; set; }

        public string? TenantId { get; set; }
    }

    internal sealed class NoOpCommandMediator : ICommandMediator
    {
        public Task SendAsync(ICommand command, CommandMediationSettings? settings = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CommandMediationSettings? settings = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<TResult>(default!);
        }
    }

    internal sealed class CancellationRecordingSerializer : IMessageSerializer
    {
        private readonly IMessageSerializer _inner;

        public CancellationRecordingSerializer(IMessageSerializer inner)
        {
            _inner = inner;
        }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            return _inner.SerializeAsync(message, cancellationToken);
        }

        public Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return _inner.DeserializeAsync(messageType, payload, cancellationToken);
        }
    }

    internal sealed class CancellationRecordingMediator : ICommandMediator
    {
        public CancellationToken LastCancellationToken { get; private set; }

        public bool WasSendCalled { get; private set; }

        public Task SendAsync(ICommand command, CommandMediationSettings? settings = null, CancellationToken cancellationToken = default)
        {
            WasSendCalled = true;
            LastCancellationToken = cancellationToken;
            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;
        }

        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CommandMediationSettings? settings = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
