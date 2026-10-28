using System.Globalization;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Auditing;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using LiteBus.Testing.Mediation;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Idempotency;

/// <summary>
///     Verifies the pipeline denies an in-flight claim and a fingerprint mismatch with a refusal code an HTTP boundary
///     can map, while a matching fingerprint replays.
/// </summary>
[Collection("Sequential")]
public sealed class IdempotencyDenialTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider with idempotency enabled and the given store registered.
    /// </summary>
    /// <param name="store">The store shared with the test.</param>
    /// <param name="applications">The counter recording how many times each handler actually ran.</param>
    /// <param name="recorder">The completion recorder to register a probe for, when the test observes completions.</param>
    /// <param name="trail">The audit trail to enable auditing for, when the test observes audit records.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        IIdempotencyStore store,
        ApplicationCounter applications,
        CompletionRecorder? recorder = null,
        RecordingAuditTrail? trail = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(applications);

        if (recorder is not null)
        {
            services.AddSingleton(recorder);
        }

        if (trail is not null)
        {
            services.AddSingleton<IAuditTrail>(trail);
        }

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<ApplyPaymentCommand>();
                    builder.Register<ApplyPaymentCommandHandler>();
                    builder.Register<ApplyPaymentCommandDefinition>();
                    builder.Register<RepeatablePaymentCommand>();
                    builder.Register<RepeatablePaymentCommandHandler>();
                    builder.Register<RepeatablePaymentCommandDefinition>();
                    builder.Register<WireTransferCommand>();
                    builder.Register<WireTransferCommandHandler>();
                    builder.Register<WireTransferCommandDefinition>();
                    builder.EnableIdempotency();

                    if (recorder is not null)
                    {
                        builder.Register<IdempotencyDenialCompletionProbe>();
                    }

                    if (trail is not null)
                    {
                        builder.Register<ApplyPaymentAuditDefinition>();
                        builder.Register<RepeatablePaymentAuditDefinition>();
                        builder.Register<WireTransferAuditDefinition>();
                        builder.EnableAuditing();
                    }
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task An_in_flight_command_is_denied_through_TrySendAsync()
    {
        var applications = new ApplicationCounter();
        var store = new InMemoryIdempotencyStore();
        var recorder = new CompletionRecorder();
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(store, applications, recorder, trail);

        // Delivery A holds the claim while B arrives for the same key.
        await store.TryClaimAsync($"{typeof(ApplyPaymentCommand).FullName}:p-1", fingerprint: null).ConfigureAwait(false);

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new ApplyPaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Code.Should().Be(IdempotencyRefusalCodes.InFlight);
        result.Reason.Should().NotBeNullOrWhiteSpace();
        applications.Count.Should().Be(0);

        recorder.Observed.Single().Context.Outcome.Should().Be(MediationOutcome.Denied);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("payments.apply-payment");
        record.Outcome.Should().Be(AuditOutcome.Denied);
    }

    [Fact]
    public async Task An_in_flight_command_throws_through_SendAsync()
    {
        var applications = new ApplicationCounter();
        var store = new InMemoryIdempotencyStore();
        var recorder = new CompletionRecorder();
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(store, applications, recorder, trail);

        await store.TryClaimAsync($"{typeof(ApplyPaymentCommand).FullName}:p-1", fingerprint: null).ConfigureAwait(false);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ApplyPaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>()
            .Where(exception => exception.Code == IdempotencyRefusalCodes.InFlight
                && exception.MessageType == typeof(ApplyPaymentCommand)).ConfigureAwait(false);

        applications.Count.Should().Be(0);

        recorder.Observed.Single().Context.Outcome.Should().Be(MediationOutcome.Denied);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("payments.apply-payment");
        record.Outcome.Should().Be(AuditOutcome.Denied);
    }

    [Fact]
    public async Task An_in_flight_result_command_is_denied_through_TrySendAsync()
    {
        var applications = new ApplicationCounter();
        var store = new InMemoryIdempotencyStore();
        var recorder = new CompletionRecorder();
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(store, applications, recorder, trail);

        await store.TryClaimAsync($"{typeof(RepeatablePaymentCommand).FullName}:p-4", fingerprint: null).ConfigureAwait(false);

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new RepeatablePaymentCommand { PaymentId = "p-4" }).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Code.Should().Be(IdempotencyRefusalCodes.InFlight);
        result.HasValue.Should().BeFalse();
        applications.Count.Should().Be(0);

        recorder.Observed.Single().Context.Outcome.Should().Be(MediationOutcome.Denied);

        var record = trail.Records.Should().ContainSingle().Subject;
        record.Action.Should().Be("payments.repeatable-payment");
        record.Outcome.Should().Be(AuditOutcome.Denied);
    }

    [Fact]
    public async Task A_fingerprint_mismatch_is_denied_through_TrySendAsync()
    {
        var applications = new ApplicationCounter();
        var recorder = new CompletionRecorder();
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications, recorder, trail);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new WireTransferCommand { TransferId = "t-1", Amount = 100m }).ConfigureAwait(false);

        // The same key with different content is not a repeat; answering it would report the wrong transfer as done.
        var result = await mediator
            .TrySendAsync(new WireTransferCommand { TransferId = "t-1", Amount = 200m }).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Code.Should().Be(IdempotencyRefusalCodes.FingerprintMismatch);
        applications.Count.Should().Be(1);

        // The first delivery succeeded and the mismatched repeat was denied; the denial is the last ending observed.
        recorder.Observed.Last().Context.Outcome.Should().Be(MediationOutcome.Denied);
        trail.Records.Last().Outcome.Should().Be(AuditOutcome.Denied);
        trail.Records.Last().Action.Should().Be("payments.wire-transfer");
    }

    [Fact]
    public async Task A_fingerprint_mismatch_throws_through_SendAsync()
    {
        var applications = new ApplicationCounter();
        var recorder = new CompletionRecorder();
        var trail = new RecordingAuditTrail();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications, recorder, trail);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new WireTransferCommand { TransferId = "t-1", Amount = 100m }).ConfigureAwait(false);

        var act = async () => await mediator
            .SendAsync(new WireTransferCommand { TransferId = "t-1", Amount = 200m }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>()
            .Where(exception => exception.Code == IdempotencyRefusalCodes.FingerprintMismatch).ConfigureAwait(false);

        applications.Count.Should().Be(1);

        recorder.Observed.Last().Context.Outcome.Should().Be(MediationOutcome.Denied);
        trail.Records.Last().Outcome.Should().Be(AuditOutcome.Denied);
        trail.Records.Last().Action.Should().Be("payments.wire-transfer");
    }

    [Fact]
    public async Task A_matching_fingerprint_replays_without_running_again()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new WireTransferCommand { TransferId = "t-2", Amount = 100m }).ConfigureAwait(false);

        var result = await mediator
            .TrySendAsync(new WireTransferCommand { TransferId = "t-2", Amount = 100m }).ConfigureAwait(false);

        result.Outcome.Should().Be(MediationOutcome.Answered);
        applications.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_stale_claim_is_taken_over_so_the_command_runs()
    {
        var time = new TestTimeProvider();
        var applications = new ApplicationCounter();
        var store = new InMemoryIdempotencyStore(time, TimeSpan.FromMinutes(5));
        var provider = BuildProvider(store, applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        // A crashed delivery left its claim unsettled; once the timeout passes the next delivery takes it over.
        await store.TryClaimAsync($"{typeof(ApplyPaymentCommand).FullName}:p-1", fingerprint: null).ConfigureAwait(false);
        time.Advance(TimeSpan.FromMinutes(6));

        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        applications.Count.Should().Be(1);

        var repeat = await mediator
            .TrySendAsync(new ApplyPaymentCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        repeat.Outcome.Should().Be(MediationOutcome.Answered);
        applications.Count.Should().Be(1);
    }
}

/// <summary>
///     A command whose declaration fingerprints the transfer amount, so the same key with different content is denied.
/// </summary>
internal sealed class WireTransferCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the transfer identifier the key is projected from.
    /// </summary>
    public string TransferId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the transfer amount the fingerprint is projected from.
    /// </summary>
    public decimal Amount { get; set; }
}

/// <summary>
///     Declares how a repeat of <see cref="WireTransferCommand" /> is recognised.
/// </summary>
internal sealed class WireTransferCommandDefinition : IIdempotencyDefinition<WireTransferCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<WireTransferCommand>(command => command.TransferId)
            .WithFingerprint<WireTransferCommand>(command => command.Amount.ToString(CultureInfo.InvariantCulture));
}

/// <summary>
///     Declares the audit position of <see cref="WireTransferCommand" />, registered only by the denial tests.
/// </summary>
internal sealed class WireTransferAuditDefinition : IAuditDefinition<WireTransferCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("payments.wire-transfer");
}

/// <summary>
///     Transfers the funds.
/// </summary>
internal sealed class WireTransferCommandHandler : ICommandHandler<WireTransferCommand>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ApplicationCounter _applications;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WireTransferCommandHandler" /> class.
    /// </summary>
    /// <param name="applications">The counter shared with the test.</param>
    public WireTransferCommandHandler(ApplicationCounter applications)
    {
        _applications = applications;
    }

    /// <inheritdoc />
    public Task HandleAsync(WireTransferCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _applications.Count++;
        return Task.CompletedTask;
    }
}
