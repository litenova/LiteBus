using System.Diagnostics.Metrics;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.Logging;

namespace LiteBus.Outbox.UnitTests.Version8;

/// <summary>
///     Verifies an immediate-addressed envelope staged with scheduled visibility is logged and counted.
/// </summary>
[Collection("Sequential")]
public sealed class ScheduledImmediateSkipTests : LiteBusTestBase
{
    /// <summary>
    ///     The contract name of the event carrying the immediate dispatch attribute.
    /// </summary>
    private const string ImmediateContractName = "tests.v8.immediate";

    /// <summary>
    ///     The contract name of the event without the immediate dispatch attribute.
    /// </summary>
    private const string PlainContractName = "tests.v8.plain";

    /// <summary>
    ///     The contract name of the event observed by the attributed handler.
    /// </summary>
    private const string HandledContractName = "tests.v8.handled";

    /// <summary>
    ///     Verifies a scheduled immediate event keeps its scheduled visibility and is logged and counted.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_immediate_event_scheduled_should_log_and_count_skip()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLogger();
        var factory = CreateFactory(clock, logger, immediateDispatch: true);
        using var probe = new SkipCounterProbe(ImmediateContractName);

        var envelope = await factory.CreateAsync(
            OutboxEnqueueItem<ImmediateEvent>.ScheduledAfter(new ImmediateEvent(), TimeSpan.FromMinutes(30))).ConfigureAwait(false);

        envelope.VisibleAfter.Should().Be(clock.GetUtcNow().Add(TimeSpan.FromMinutes(30)));
        logger.Entries.Should().ContainSingle();
        var entry = logger.Entries[0];
        entry.Level.Should().Be(LogLevel.Debug);
        entry.EventId.Id.Should().Be(2013);
        entry.EventId.Name.Should().Be("ImmediateDispatchScheduledSkipped");
        entry.Message.Should().Contain(ImmediateContractName);
        probe.Count.Should().Be(1);
    }

    /// <summary>
    ///     Verifies an immediate event with immediate visibility is marked without a log or a count.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_immediate_event_immediate_should_mark_without_log_or_count()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLogger();
        var factory = CreateFactory(clock, logger, immediateDispatch: true);
        using var probe = new SkipCounterProbe(ImmediateContractName);

        var envelope = await factory.CreateAsync(OutboxEnqueueItem<ImmediateEvent>.From(new ImmediateEvent())).ConfigureAwait(false);

        envelope.VisibleAfter.Should().Be(clock.GetUtcNow().Add(TimeSpan.FromSeconds(30)));
        logger.Entries.Should().BeEmpty();
        probe.Count.Should().Be(0);
    }

    /// <summary>
    ///     Verifies a scheduled event without the attribute is staged silently.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_plain_event_scheduled_should_skip_silently()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLogger();
        var factory = CreateFactory(clock, logger, immediateDispatch: true);
        using var probe = new SkipCounterProbe(PlainContractName);

        var envelope = await factory.CreateAsync(
            OutboxEnqueueItem<PlainEvent>.ScheduledAfter(new PlainEvent(), TimeSpan.FromMinutes(30))).ConfigureAwait(false);

        envelope.VisibleAfter.Should().Be(clock.GetUtcNow().Add(TimeSpan.FromMinutes(30)));
        logger.Entries.Should().BeEmpty();
        probe.Count.Should().Be(0);
    }

    /// <summary>
    ///     Verifies a scheduled immediate event is staged silently when immediate dispatch is off.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_immediate_dispatch_disabled_should_skip_silently()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLogger();
        var factory = CreateFactory(clock, logger, immediateDispatch: false);
        using var probe = new SkipCounterProbe(ImmediateContractName);

        var envelope = await factory.CreateAsync(
            OutboxEnqueueItem<ImmediateEvent>.ScheduledAfter(new ImmediateEvent(), TimeSpan.FromMinutes(30))).ConfigureAwait(false);

        envelope.VisibleAfter.Should().Be(clock.GetUtcNow().Add(TimeSpan.FromMinutes(30)));
        logger.Entries.Should().BeEmpty();
        probe.Count.Should().Be(0);
    }

    /// <summary>
    ///     Verifies a scheduled envelope addressed to an attributed handler names the subscriber in the log.
    /// </summary>
    [Fact]
    public async Task CreateManyAsync_when_immediate_handler_scheduled_should_name_subscriber()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLogger();
        var contracts = new MessageContractRegistry();
        contracts.Register<HandledEvent>(HandledContractName);
        var messages = new MessageRegistry();
        messages.Register(typeof(ImmediateReaction));

        var factory = new OutboxEnvelopeFactory(
            contracts,
            new SystemTextJsonMessageSerializer(),
            clock,
            null,
            null,
            messages,
            new OutboxEnqueueOptions { PerHandlerDelivery = true },
            new OutboxImmediateDispatchOptions(),
            null,
            logger);
        using var probe = new SkipCounterProbe(HandledContractName);

        var envelopes = await factory.CreateManyAsync(
            OutboxEnqueueItem<HandledEvent>.ScheduledAfter(new HandledEvent(), TimeSpan.FromMinutes(30))).ConfigureAwait(false);

        envelopes.Should().ContainSingle();
        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Message.Should().Contain(typeof(ImmediateReaction).FullName);
        probe.Count.Should().Be(1);
    }

    private static OutboxEnvelopeFactory CreateFactory(TimeProvider clock, RecordingLogger logger, bool immediateDispatch)
    {
        var contracts = new MessageContractRegistry();
        contracts.Register<ImmediateEvent>(ImmediateContractName);
        contracts.Register<PlainEvent>(PlainContractName);

        return new OutboxEnvelopeFactory(
            contracts,
            new SystemTextJsonMessageSerializer(),
            clock,
            null,
            null,
            null,
            new OutboxEnqueueOptions(),
            immediateDispatch ? new OutboxImmediateDispatchOptions() : null,
            null,
            logger);
    }

    /// <summary>
    ///     An event type marked for immediate dispatch.
    /// </summary>
    [DispatchImmediately]
    private sealed record ImmediateEvent;

    /// <summary>
    ///     An event type without the immediate dispatch attribute.
    /// </summary>
    private sealed record PlainEvent;

    /// <summary>
    ///     An event observed by the attributed handler.
    /// </summary>
    private sealed record HandledEvent;

    /// <summary>
    ///     A handler marked for immediate dispatch.
    /// </summary>
    [DispatchImmediately]
    private sealed class ImmediateReaction : IEventHandler<HandledEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(HandledEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<OutboxEnvelopeFactory>
    {
        public List<LogEntry> Entries { get; } = [];

        IDisposable ILogger.BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
        }

        internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class SkipCounterProbe : IDisposable
    {
        private readonly string _contractName;
        private readonly MeterListener _listener;

        public SkipCounterProbe(string contractName)
        {
            _contractName = contractName;

            MeterListener? listener = null;
            listener = new MeterListener
            {
                InstrumentPublished = (instrument, _) =>
                {
                    if (instrument.Meter.Name == LiteBusOutboxTelemetry.MeterName &&
                        instrument.Name == LiteBusOutboxTelemetry.ImmediateDispatchSkippedScheduledInstrumentName)
                    {
                        listener!.EnableMeasurementEvents(instrument);
                    }
                }
            };

            listener.SetMeasurementEventCallback<long>(OnMeasurement);
            listener.Start();
            _listener = listener;
        }

        public long Count { get; private set; }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private void OnMeasurement(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == LiteBusOutboxTelemetry.ImmediateDispatchContractAttributeName &&
                    tag.Value is string contractName &&
                    contractName == _contractName)
                {
                    Count += measurement;
                    return;
                }
            }
        }
    }
}
