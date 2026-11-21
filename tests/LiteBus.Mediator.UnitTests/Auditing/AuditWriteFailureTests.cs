using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     Verifies how a failed audit write ends a command mediation, with and without <see cref="AuditingModuleBuilder.RequireRecord" />.
/// </summary>
/// <remarks>
///     These tests compose a real container with an application trail whose write always throws, so the guard's policy
///     is observed at the mediator boundary rather than by calling the internal guard directly.
/// </remarks>
[Collection("Sequential")]
public sealed class AuditWriteFailureTests : LiteBusTestBase
{
    /// <summary>
    ///     The action code of the command used by these tests, so the meter probe only sees these tests' writes.
    /// </summary>
    private const string WriteRecordAction = "tests.audit-write-failure";

    /// <summary>
    ///     Verifies a denial keeps its own exception when its audit record cannot be written.
    /// </summary>
    [Fact]
    public async Task A_denied_command_keeps_the_denial_and_logs_counts_the_failed_record()
    {
        var logger = new RecordingLoggerProvider();
        using var provider = BuildProvider(requireRecord: false, logger, new ThrowingAuditTrail());
        using var probe = new WriteFailureCounterProbe();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new WriteRecordCommand { ShouldDeny = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error && entry.EventId.Id == 2720);

        var measurement = probe.Measurements.Should().ContainSingle().Subject;
        measurement.Value.Should().Be(1);
        measurement.Outcome.Should().Be(nameof(AuditOutcome.Denied));
    }

    /// <summary>
    ///     Verifies a success still succeeds when its audit record cannot be written and the record is not required.
    /// </summary>
    [Fact]
    public async Task A_successful_command_succeeds_without_RequireRecord_when_the_write_fails()
    {
        var logger = new RecordingLoggerProvider();
        using var provider = BuildProvider(requireRecord: false, logger, new ThrowingAuditTrail());

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new WriteRecordCommand()).ConfigureAwait(false);

        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error && entry.EventId.Id == 2720);
    }

    /// <summary>
    ///     Verifies a success faults with the record-not-written code when the record is required and cannot be written.
    /// </summary>
    [Fact]
    public async Task A_successful_command_faults_with_RequireRecord_when_the_write_fails()
    {
        var logger = new RecordingLoggerProvider();
        using var provider = BuildProvider(requireRecord: true, logger, new ThrowingAuditTrail());

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new WriteRecordCommand()).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<MessageFaultException>().ConfigureAwait(false);

        thrown.Which.Code.Should().Be("audit.record-not-written");
        thrown.Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("trail down");

        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error && entry.EventId.Id == 2720);
    }

    /// <summary>
    ///     Verifies a denial keeps its own exception with <see cref="AuditingModuleBuilder.RequireRecord" /> even when
    ///     its audit record cannot be written.
    /// </summary>
    [Fact]
    public async Task A_denied_command_keeps_the_denial_with_RequireRecord_when_the_write_fails()
    {
        var logger = new RecordingLoggerProvider();
        using var provider = BuildProvider(requireRecord: true, logger, new ThrowingAuditTrail());

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new WriteRecordCommand { ShouldDeny = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error && entry.EventId.Id == 2720);
    }

    /// <summary>
    ///     Builds a provider with auditing and the test command registered.
    /// </summary>
    /// <param name="requireRecord">Whether the composition requires a record for every successful mediation.</param>
    /// <param name="logger">The logger provider that captures the failed-write event.</param>
    /// <param name="trail">The trail that receives the record.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(bool requireRecord, RecordingLoggerProvider logger, IAuditTrail trail)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .AddProvider(logger)
            .SetMinimumLevel(LogLevel.Trace));

        return services
            .AddLiteBus(registry =>
            {
                registry.AddAuditing(auditing =>
                {
                    auditing.UseTrailInstance(trail).ForCommands();

                    if (requireRecord)
                    {
                        auditing.RequireRecord();
                    }
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<WriteRecordCommand>();
                    builder.Register<WriteRecordCommandGuard>();
                    builder.Register<WriteRecordCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     A command whose audit position is declared by attribute.
    /// </summary>
    [Audited(WriteRecordAction, Category = "tests")]
    private sealed class WriteRecordCommand : ICommand
    {
        /// <summary>
        ///     Gets or sets a value indicating whether the guard denies the command.
        /// </summary>
        public bool ShouldDeny { get; set; }
    }

    /// <summary>
    ///     Denies the command when it asks to be denied.
    /// </summary>
    private sealed class WriteRecordCommandGuard : ICommandGuard<WriteRecordCommand>
    {
        /// <inheritdoc />
        public Task<Verdict> DecideAsync(
            WriteRecordCommand message,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(message.ShouldDeny
                ? Verdict.Deny("denied for the audit write failure test")
                : Verdict.Allow);
        }
    }

    /// <summary>
    ///     Completes the command without doing any work.
    /// </summary>
    private sealed class WriteRecordCommandHandler : ICommandHandler<WriteRecordCommand>
    {
        /// <inheritdoc />
        public Task HandleAsync(WriteRecordCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     An audit trail that fails every write.
    /// </summary>
    private sealed class ThrowingAuditTrail : IAuditTrail
    {
        /// <inheritdoc />
        public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("trail down");
        }
    }

    /// <summary>
    ///     A logger provider that records the log entries the auditing module writes.
    /// </summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        /// <summary>
        ///     Gets the entries recorded so far.
        /// </summary>
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(this);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <summary>
        ///     A logger that records every entry on its provider.
        /// </summary>
        private sealed class RecordingLogger : ILogger
        {
            /// <summary>
            ///     The provider that receives the recorded entries.
            /// </summary>
            private readonly RecordingLoggerProvider _provider;

            /// <summary>
            ///     Initializes a new instance of the <see cref="RecordingLogger" /> class.
            /// </summary>
            /// <param name="provider">The provider that receives the recorded entries.</param>
            public RecordingLogger(RecordingLoggerProvider provider)
            {
                _provider = provider;
            }

            /// <inheritdoc />
            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            /// <inheritdoc />
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _provider.Entries.Enqueue(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
            }

            /// <summary>
            ///     A no-op logging scope.
            /// </summary>
            private sealed class NullScope : IDisposable
            {
                /// <summary>
                ///     Gets the shared scope instance.
                /// </summary>
                public static NullScope Instance { get; } = new();

                /// <inheritdoc />
                public void Dispose()
                {
                }
            }
        }

        /// <summary>
        ///     One recorded log entry.
        /// </summary>
        /// <param name="Level">The level the entry was written at.</param>
        /// <param name="EventId">The identifier of the event.</param>
        /// <param name="Message">The formatted message.</param>
        /// <param name="Exception">The exception carried by the entry, when any.</param>
        internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
    }

    /// <summary>
    ///     Captures measurements of the audit write failures counter for this test's action.
    /// </summary>
    private sealed class WriteFailureCounterProbe : IDisposable
    {
        /// <summary>
        ///     The listener subscribed to the auditing meter.
        /// </summary>
        private readonly MeterListener _listener;

        /// <summary>
        ///     The measurements captured for <see cref="WriteRecordAction" />.
        /// </summary>
        private readonly ConcurrentQueue<RecordedMeasurement> _measurements = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="WriteFailureCounterProbe" /> class.
        /// </summary>
        public WriteFailureCounterProbe()
        {
            MeterListener? listener = null;
            listener = new MeterListener
            {
                InstrumentPublished = (instrument, _) =>
                {
                    if (instrument.Meter.Name == LiteBusAuditingTelemetry.MeterName &&
                        instrument.Name == LiteBusAuditingTelemetry.WriteFailuresInstrumentName)
                    {
                        listener!.EnableMeasurementEvents(instrument);
                    }
                }
            };

            listener.SetMeasurementEventCallback<long>(OnMeasurement);
            listener.Start();
            _listener = listener;
        }

        /// <summary>
        ///     Gets the measurements captured so far.
        /// </summary>
        public IReadOnlyList<RecordedMeasurement> Measurements => _measurements.ToArray();

        /// <inheritdoc />
        public void Dispose()
        {
            _listener.Dispose();
        }

        /// <summary>
        ///     Records a measurement for this test's action.
        /// </summary>
        /// <param name="instrument">The instrument that recorded the measurement.</param>
        /// <param name="measurement">The recorded value.</param>
        /// <param name="tags">The tags attached to the measurement.</param>
        /// <param name="state">The listener state.</param>
        private void OnMeasurement(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            string? action = null;
            string? outcome = null;

            foreach (var tag in tags)
            {
                if (tag.Key == LiteBusAuditingTelemetry.ActionAttributeName && tag.Value is string capturedAction)
                {
                    action = capturedAction;
                }

                if (tag.Key == LiteBusAuditingTelemetry.OutcomeAttributeName && tag.Value is string capturedOutcome)
                {
                    outcome = capturedOutcome;
                }
            }

            if (action == WriteRecordAction)
            {
                _measurements.Enqueue(new RecordedMeasurement(measurement, outcome, action));
            }
        }

        /// <summary>
        ///     One captured counter measurement.
        /// </summary>
        /// <param name="Value">The measured value.</param>
        /// <param name="Outcome">The audit outcome tag.</param>
        /// <param name="Action">The audit action tag.</param>
        internal sealed record RecordedMeasurement(long Value, string? Outcome, string? Action);
    }
}
