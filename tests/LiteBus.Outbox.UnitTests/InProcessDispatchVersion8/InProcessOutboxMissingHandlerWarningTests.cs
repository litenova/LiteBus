using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiteBus.Outbox.UnitTests.InProcessDispatchVersion8;

/// <summary>
///     Verifies the startup warning for in-process outbox dispatch without a required handler.
/// </summary>
public sealed class InProcessOutboxMissingHandlerWarningTests
{
    /// <summary>
    ///     Verifies the module records the warning task in the host manifest.
    /// </summary>
    [Fact]
    public void UseInProcessDispatch_should_register_warning_startup_task()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(_ =>
                {
                });

                registry.AddOutbox(outbox =>
                {
                    outbox.UseInMemoryStorage();
                    outbox.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var manifest = serviceProvider.GetRequiredService<LiteBusHostManifest>();

        manifest.StartupTasks.Should().Contain(typeof(InProcessOutboxMissingHandlerWarningStartupTask));
    }

    /// <summary>
    ///     Verifies no warning is logged while a handler is required.
    /// </summary>
    [Fact]
    public async Task RunAsync_when_handler_required_should_log_nothing()
    {
        var logger = new RecordingLogger();
        var task = new InProcessOutboxMissingHandlerWarningStartupTask(new InProcessOutboxDispatchOptions(), logger);

        await task.RunAsync().ConfigureAwait(false);

        logger.Entries.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies the warning names the recorded reason.
    /// </summary>
    [Fact]
    public async Task RunAsync_when_envelopes_without_handler_allowed_should_log_reason()
    {
        const string reason = "Leaf service with no local subscribers.";
        var logger = new RecordingLogger();
        var options = new InProcessOutboxDispatchOptions
        {
            RequireHandler = false,
            MissingHandlerReason = reason
        };
        var task = new InProcessOutboxMissingHandlerWarningStartupTask(options, logger);

        await task.RunAsync().ConfigureAwait(false);

        logger.Entries.Should().ContainSingle();
        var entry = logger.Entries[0];
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(reason);
    }

    /// <summary>
    ///     Verifies the warning still runs when the reason was not recorded.
    /// </summary>
    [Fact]
    public async Task RunAsync_when_reason_missing_should_log_without_throwing()
    {
        var logger = new RecordingLogger();
        var options = new InProcessOutboxDispatchOptions
        {
            RequireHandler = false
        };
        var task = new InProcessOutboxMissingHandlerWarningStartupTask(options, logger);

        await task.RunAsync().ConfigureAwait(false);

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
    }

    /// <summary>
    ///     Verifies null options are rejected.
    /// </summary>
    [Fact]
    public void Constructor_with_null_options_should_throw()
    {
        var act = () => new InProcessOutboxMissingHandlerWarningStartupTask(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class RecordingLogger : ILogger<InProcessOutboxMissingHandlerWarningStartupTask>
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
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
            ArgumentNullException.ThrowIfNull(formatter);

            _entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
