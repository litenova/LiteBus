using System.Diagnostics;
using System.Diagnostics.Metrics;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that in-process mediation is observable: one span per message, a duration and an outcome, and a
///     decision counter that names the stage that stopped it.
/// </summary>
/// <remarks>
///     The inbox, the outbox, the transport and each broker adapter all published instruments while the library's
///     primary job published none, so "which stage denied this" was answerable only from a stack trace.
/// </remarks>
[Collection("Sequential")]
public sealed class MediationTelemetryTests : LiteBusTestBase
{
    [Fact]
    public async Task One_span_is_started_per_mediation_and_carries_the_outcome()
    {
        using var listener = new RecordedActivities();
        var provider = Build(new StageActivityRecorder());

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SteeredCommand()).ConfigureAwait(false);

        var activity = listener.Activities.Should().ContainSingle().Subject;
        activity.OperationName.Should().Be("mediate SteeredCommand");
        activity.GetTagItem(LiteBusMediationTelemetry.MessageAttributeName).Should().Be("SteeredCommand");
        activity.GetTagItem(LiteBusMediationTelemetry.OutcomeAttributeName).Should().Be("Succeeded");
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task A_denied_mediation_records_the_code_and_is_not_marked_as_an_error()
    {
        using var listener = new RecordedActivities();
        var provider = Build(new StageActivityRecorder { Deny = true });

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SteeredCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        var activity = listener.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem(LiteBusMediationTelemetry.OutcomeAttributeName).Should().Be("Denied");
        activity.GetTagItem(LiteBusMediationTelemetry.CodeAttributeName).Should().Be("NOT_PERMITTED");

        // A denial is a decision, not a fault. Colouring every refused request red makes a trace view useless for
        // finding the requests that actually broke.
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task A_faulted_mediation_marks_its_span_as_an_error()
    {
        using var listener = new RecordedActivities();
        var provider = Build(new StageActivityRecorder(), typeof(ThrowingSteeredPostHandler));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SteeredCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        listener.Activities.Should().ContainSingle()
            .Which.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task The_duration_histogram_and_outcome_counter_record_every_mediation()
    {
        using var meter = new RecordedMeasurements();
        var provider = Build(new StageActivityRecorder());

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SteeredCommand()).ConfigureAwait(false);

        meter.Instruments.Should().Contain(LiteBusMediationTelemetry.DurationInstrumentName);
        meter.Instruments.Should().Contain(LiteBusMediationTelemetry.CountInstrumentName);
        meter.TagsFor(LiteBusMediationTelemetry.CountInstrumentName)
            .Should().Contain(LiteBusMediationTelemetry.OutcomeAttributeName);
    }

    [Fact]
    public async Task The_decision_counter_names_the_stage_that_stopped_the_mediation()
    {
        using var meter = new RecordedMeasurements();
        var provider = Build(new StageActivityRecorder { Deny = true });

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SteeredCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        // The measurement that turns "which stage denied this" from a stack trace into a filter.
        meter.Instruments.Should().Contain(LiteBusMediationTelemetry.DecisionsInstrumentName);
        meter.TagValuesFor(LiteBusMediationTelemetry.DecisionsInstrumentName, LiteBusMediationTelemetry.StageAttributeName)
            .Should().Contain("guard");
    }

    [Fact]
    public async Task A_mediation_that_nothing_stopped_records_no_decision()
    {
        using var meter = new RecordedMeasurements();
        var provider = Build(new StageActivityRecorder());

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SteeredCommand()).ConfigureAwait(false);

        meter.Instruments.Should().NotContain(LiteBusMediationTelemetry.DecisionsInstrumentName);
    }

    [Fact]
    public async Task Per_stage_durations_are_off_until_asked_for()
    {
        using var meter = new RecordedMeasurements();
        var provider = Build(new StageActivityRecorder());

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SteeredCommand()).ConfigureAwait(false);

        // Mediation volume is orders of magnitude above durable-processing volume, so one measurement per stage per
        // message is opt-in.
        meter.Instruments.Should().NotContain(LiteBusMediationTelemetry.StageDurationInstrumentName);
    }

    [Fact]
    public async Task Per_stage_durations_are_recorded_when_asked_for()
    {
        using var meter = new RecordedMeasurements();

        var provider = Build(
            new StageActivityRecorder(),
            telemetry: new MediationTelemetryOptions { StageMetrics = true });

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SteeredCommand()).ConfigureAwait(false);

        meter.Instruments.Should().Contain(LiteBusMediationTelemetry.StageDurationInstrumentName);
        meter.TagValuesFor(LiteBusMediationTelemetry.StageDurationInstrumentName, LiteBusMediationTelemetry.StageAttributeName)
            .Should().Contain("guard", "validator", "shortcut", "prehandler");
    }

    [Fact]
    public async Task Disabling_telemetry_records_nothing()
    {
        using var listener = new RecordedActivities();
        using var meter = new RecordedMeasurements();

        var provider = Build(new StageActivityRecorder(), telemetry: MediationTelemetryOptions.Disabled);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SteeredCommand()).ConfigureAwait(false);

        listener.Activities.Should().BeEmpty();
        meter.Instruments.Should().BeEmpty();
    }

    /// <summary>
    ///     Builds a provider over the steered command with the requested telemetry configuration.
    /// </summary>
    /// <param name="recorder">The recorder shared with every stage.</param>
    /// <param name="extras">Extra command types to register.</param>
    /// <param name="telemetry">The telemetry configuration, or null for the defaults.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(
        StageActivityRecorder recorder,
        Type? extras = null,
        MediationTelemetryOptions? telemetry = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                {
                    // Reset explicitly rather than relying on order: the options are process-wide, because an
                    // ActivitySource and a Meter are, so a previous test's choice would otherwise leak into this one.
                    messaging.UseTelemetry(telemetry ?? new MediationTelemetryOptions());
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<SteeredCommand>();
                    builder.Register<SteeredCommandHandler>();
                    builder.Register(typeof(SteeredGuard<>));
                    builder.Register(typeof(SteeredValidator<>));
                    builder.Register<SteeredShortcut>();
                    builder.Register<SteeredPreHandler>();

                    if (extras is not null)
                    {
                        builder.Register(extras);
                    }
                });
            })
            .BuildServiceProvider();
    }
}
