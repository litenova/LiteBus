using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests.Lineage;

/// <summary>
///     Verifies the in-process inbox dispatcher executes every envelope as a root mediation with a cause.
/// </summary>
[Collection("Sequential")]
public sealed class InboxDispatcherLineageTests : LiteBusTestBase
{
    [Fact]
    public async Task DispatchAsync_SetsCauseFromEnvelopeTraceFields()
    {
        var mediator = new RecordingCommandMediator();
        var dispatcher = CreateDispatcher(mediator);
        var envelopeId = Guid.NewGuid();
        var payload = await SerializeAsync(new LineageProbeCommand()).ConfigureAwait(false);

        await dispatcher.DispatchAsync(CreateEnvelope(envelopeId, payload, "correlation-42", "causation-7", "tenant-west")).ConfigureAwait(false);

        mediator.Sent.Should().ContainSingle();
        mediator.LastSettings.Should().NotBeNull();
        mediator.LastSettings!.Cause.Should().Be(new MediationCause("correlation-42", "causation-7", MediationCauseSource.Inbox));
        mediator.LastSettings.Identity.Should().Be(ExecutionIdentity.ForTenant("tenant-west"));
    }

    [Fact]
    public async Task DispatchAsync_WithoutEnvelopeTraceFields_UsesEnvelopeIdentifierAsCause()
    {
        var mediator = new RecordingCommandMediator();
        var dispatcher = CreateDispatcher(mediator);
        var envelopeId = Guid.NewGuid();
        var payload = await SerializeAsync(new LineageProbeCommand()).ConfigureAwait(false);

        await dispatcher.DispatchAsync(CreateEnvelope(envelopeId, payload, null, null, null)).ConfigureAwait(false);

        var fallback = envelopeId.ToString("D");
        mediator.LastSettings.Should().NotBeNull();
        mediator.LastSettings!.Cause.Should().Be(new MediationCause(fallback, fallback, MediationCauseSource.Inbox));
    }

    [Fact]
    public async Task DispatchAsync_WithPartialEnvelopeTraceFields_FallsBackToEnvelopeIdentifier()
    {
        var mediator = new RecordingCommandMediator();
        var dispatcher = CreateDispatcher(mediator);
        var envelopeId = Guid.NewGuid();
        var payload = await SerializeAsync(new LineageProbeCommand()).ConfigureAwait(false);

        await dispatcher.DispatchAsync(CreateEnvelope(envelopeId, payload, "correlation-42", null, null)).ConfigureAwait(false);

        mediator.LastSettings.Should().NotBeNull();
        mediator.LastSettings!.Cause.Should().Be(new MediationCause("correlation-42", envelopeId.ToString("D"), MediationCauseSource.Inbox));
    }

    [Fact]
    public async Task DispatchAsync_InsideAMediation_ExecutesAsRootMediation()
    {
        var lineage = new LineageCapture();
        var fixture = new NestedDispatchFixture();
        var serializer = new SystemTextJsonMessageSerializer();
        var payload = await serializer.SerializeAsync(new LineageProbeCommand()).ConfigureAwait(false);

        fixture.InnerEnvelope = CreateEnvelope(Guid.NewGuid(), payload, "correlation-42", "causation-7", "tenant-west");

        var provider = new ServiceCollection()
            .AddSingleton(lineage)
            .AddSingleton(fixture)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<OuterLineageCommandHandler>();
                    builder.Register<InnerLineageCommandHandler>();
                });
            })
            .BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var contracts = new MessageContractRegistry();
            contracts.Register<LineageProbeCommand>("lineage.commands.probe");
            fixture.Dispatcher = new CommandInboxDispatcher(
                provider.GetRequiredService<ICommandMediator>(),
                contracts,
                serializer);

            await provider.GetRequiredService<ICommandMediator>().SendAsync(new OuterLineageCommand()).ConfigureAwait(false);

            lineage.InnerParent.Should().BeNull();
            lineage.InnerCause.Should().Be(new MediationCause("correlation-42", "causation-7", MediationCauseSource.Inbox));
            lineage.OuterParent.Should().BeNull();
            lineage.OuterCause.Should().BeNull();
            lineage.AmbientRestored.Should().BeTrue();
        }
    }

    private static CommandInboxDispatcher CreateDispatcher(RecordingCommandMediator mediator)
    {
        var contracts = new MessageContractRegistry();
        contracts.Register<LineageProbeCommand>("lineage.commands.probe");

        return new CommandInboxDispatcher(mediator, contracts, new SystemTextJsonMessageSerializer());
    }

    private static Task<string> SerializeAsync(LineageProbeCommand command)
    {
        return new SystemTextJsonMessageSerializer().SerializeAsync(command);
    }

    private static InboxEnvelope CreateEnvelope(
        Guid id,
        string payload,
        string? correlationId,
        string? causationId,
        string? tenantId)
    {
        return new InboxEnvelope
        {
            Id = id,
            ContractName = "lineage.commands.probe",
            ContractVersion = 1,
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            CorrelationId = correlationId,
            CausationId = causationId,
            TenantId = tenantId
        };
    }

    public sealed record LineageProbeCommand : ICommand;

    public sealed record OuterLineageCommand : ICommand;

    public sealed class RecordingCommandMediator : ICommandMediator
    {
        private readonly List<ICommand> _sent = [];

        public IReadOnlyList<ICommand> Sent => _sent;

        public CommandMediationSettings? LastSettings { get; private set; }

        public Task SendAsync(ICommand command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default)
        {
            _sent.Add(command);
            LastSettings = commandMediationSettings;
            return Task.CompletedTask;
        }

        public Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<MediationResult> TrySendAsync(ICommand command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MediationResult.Succeeded());
        }

        public Task<MediationResult<TCommandResult>> TrySendAsync<TCommandResult>(ICommand<TCommandResult> command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MediationResult<TCommandResult>.Succeeded(default!));
        }

        public Task<MediationDecision> EvaluateAsync(ICommand command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MediationDecision.Allowed);
        }
    }

    public sealed class NestedDispatchFixture
    {
        public InboxEnvelope InnerEnvelope { get; set; } = null!;

        public CommandInboxDispatcher Dispatcher { get; set; } = null!;
    }

    public sealed class LineageCapture
    {
        public IExecutionContext? InnerParent { get; set; }

        public MediationCause? InnerCause { get; set; }

        public IExecutionContext? OuterParent { get; set; }

        public MediationCause? OuterCause { get; set; }

        public bool AmbientRestored { get; set; }
    }

    public sealed class OuterLineageCommandHandler : ICommandHandler<OuterLineageCommand>
    {
        private readonly NestedDispatchFixture _fixture;
        private readonly LineageCapture _capture;

        public OuterLineageCommandHandler(NestedDispatchFixture fixture, LineageCapture capture)
        {
            _fixture = fixture;
            _capture = capture;
        }

        public async Task HandleAsync(OuterLineageCommand message, CancellationToken cancellationToken = default)
        {
            var ambientBefore = AmbientExecutionContext.Current;
            _capture.OuterParent = ambientBefore.Parent;
            _capture.OuterCause = ambientBefore.Cause;

            await _fixture.Dispatcher.DispatchAsync(_fixture.InnerEnvelope, cancellationToken).ConfigureAwait(false);

            _capture.AmbientRestored = ReferenceEquals(AmbientExecutionContext.Current, ambientBefore);
        }
    }

    public sealed class InnerLineageCommandHandler : ICommandHandler<LineageProbeCommand>
    {
        private readonly LineageCapture _capture;

        public InnerLineageCommandHandler(LineageCapture capture)
        {
            _capture = capture;
        }

        public Task HandleAsync(LineageProbeCommand message, CancellationToken cancellationToken = default)
        {
            var current = AmbientExecutionContext.Current;
            _capture.InnerParent = current.Parent;
            _capture.InnerCause = current.Cause;
            return Task.CompletedTask;
        }
    }
}
