using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;
using LiteBus.Auditing.Linking;
using Microsoft.Extensions.Logging;

namespace LiteBus.Auditing.UnitTests.Linking;

/// <summary>
///     Verifies that the link sweep loop sweeps on its interval and survives store failures.
/// </summary>
public sealed class AuditLinkSweepBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_sweeps_with_the_configured_age_until_cancelled()
    {
        var linker = new RecordingLinker();
        var options = new AuditLinkSweepHostOptions
        {
            Interval = TimeSpan.FromMilliseconds(20),
            OlderThan = TimeSpan.FromMinutes(1),
        };
        var service = new AuditLinkSweepBackgroundService(linker, options, TimeProvider.System);
        using var stopping = new CancellationTokenSource();

        var running = service.ExecuteAsync(stopping.Token);

        var first = await WaitForSweepAsync(linker, 1).ConfigureAwait(false);
        first.Should().Be(TimeSpan.FromMinutes(1));

        await stopping.CancelAsync().ConfigureAwait(false);
        await running.ConfigureAwait(false);

        linker.SweepCalls.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_returns_without_sweeping_when_disabled()
    {
        var linker = new RecordingLinker();
        var options = new AuditLinkSweepHostOptions { Enabled = false };
        var service = new AuditLinkSweepBackgroundService(linker, options, TimeProvider.System);

        await service.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);

        linker.SweepCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_retries_after_a_sweep_failure()
    {
        var linker = new RecordingLinker { FailuresBeforeSuccess = 1 };
        var options = new AuditLinkSweepHostOptions
        {
            Interval = TimeSpan.FromMilliseconds(20),
            OlderThan = TimeSpan.Zero,
        };
        var service = new AuditLinkSweepBackgroundService(linker, options, TimeProvider.System);
        using var stopping = new CancellationTokenSource();

        var running = service.ExecuteAsync(stopping.Token);

        await WaitForSweepAsync(linker, 2).ConfigureAwait(false);

        await stopping.CancelAsync().ConfigureAwait(false);
        await running.ConfigureAwait(false);

        linker.SweepCalls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ExecuteAsync_logs_a_sweep_failure_and_keeps_running()
    {
        var linker = new RecordingLinker { FailuresBeforeSuccess = 1 };
        var options = new AuditLinkSweepHostOptions
        {
            Interval = TimeSpan.FromMilliseconds(20),
            OlderThan = TimeSpan.Zero,
        };
        var logger = new RecordingLogger<AuditLinkSweepBackgroundService>();
        var service = new AuditLinkSweepBackgroundService(linker, options, TimeProvider.System, logger);
        using var stopping = new CancellationTokenSource();

        var running = service.ExecuteAsync(stopping.Token);

        await WaitForSweepAsync(linker, 2).ConfigureAwait(false);

        await stopping.CancelAsync().ConfigureAwait(false);
        await running.ConfigureAwait(false);

        logger.Errors.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_throws_for_an_invalid_interval()
    {
        var linker = new RecordingLinker();
        var options = new AuditLinkSweepHostOptions { Interval = TimeSpan.Zero };
        var service = new AuditLinkSweepBackgroundService(linker, options, TimeProvider.System);

        var act = () => service.ExecuteAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    [Fact]
    public void Sweep_options_accept_the_defaults()
    {
        var options = new AuditLinkSweepHostOptions();

        options.Enabled.Should().BeTrue();
        options.Interval.Should().Be(TimeSpan.FromMinutes(1));
        options.OlderThan.Should().Be(TimeSpan.FromMinutes(1));

        options.Validate();
    }

    [Fact]
    public void Sweep_options_accept_a_zero_age()
    {
        var options = new AuditLinkSweepHostOptions { OlderThan = TimeSpan.Zero };

        options.Validate();
    }

    [Fact]
    public void Sweep_options_reject_a_negative_age()
    {
        var options = new AuditLinkSweepHostOptions { OlderThan = TimeSpan.FromMinutes(-1) };

        var act = () => options.Validate();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Waits until the stub linker recorded the given sweep call.
    /// </summary>
    /// <param name="linker">The stub linker to wait on.</param>
    /// <param name="calls">How many sweep calls to wait for.</param>
    /// <returns>The age the awaited sweep call swept with.</returns>
    private static async Task<TimeSpan> WaitForSweepAsync(RecordingLinker linker, int calls)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        return await linker.SweptAsync(calls, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>
    ///     A linker stub that records sweep calls and fails a fixed number of times first.
    /// </summary>
    private sealed class RecordingLinker : IAuditLinker
    {
        /// <summary>
        ///     Guards the sweep record below.
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        ///     The ages every sweep call swept with, in call order.
        /// </summary>
        private readonly List<TimeSpan> _ages = [];

        /// <summary>
        ///     The completion waiting for the next sweep call.
        /// </summary>
        private TaskCompletionSource<TimeSpan>? _nextSweep;

        /// <summary>
        ///     Gets or sets how many sweep calls fail before one succeeds.
        /// </summary>
        public int FailuresBeforeSuccess { get; set; }

        /// <summary>
        ///     Gets how many sweep calls ran.
        /// </summary>
        public int SweepCalls
        {
            get
            {
                lock (_lock)
                {
                    return _ages.Count;
                }
            }
        }

        /// <inheritdoc />
        public ValueTask LinkPendingAsync(string segment, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask<int> SweepAsync(TimeSpan olderThan, CancellationToken cancellationToken)
        {
            TaskCompletionSource<TimeSpan>? waiting;

            lock (_lock)
            {
                if (FailuresBeforeSuccess > 0)
                {
                    FailuresBeforeSuccess--;
                    _ages.Add(olderThan);
                    waiting = _nextSweep;
                    _nextSweep = null;
                    waiting?.SetResult(olderThan);

                    throw new InvalidOperationException("The audit store is unavailable.");
                }

                _ages.Add(olderThan);
                waiting = _nextSweep;
                _nextSweep = null;
            }

            waiting?.SetResult(olderThan);

            return ValueTask.FromResult(0);
        }

        /// <summary>
        ///     Waits until the given sweep call ran.
        /// </summary>
        /// <param name="calls">How many sweep calls to wait for.</param>
        /// <param name="cancellationToken">The token used to cancel the wait.</param>
        /// <returns>The age the awaited sweep call swept with.</returns>
        public Task<TimeSpan> SweptAsync(int calls, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_ages.Count >= calls)
                {
                    return Task.FromResult(_ages[calls - 1]);
                }

                _nextSweep ??= new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);

                return WaitForCountAsync(calls, cancellationToken);
            }
        }

        /// <summary>
        ///     Waits outside the lock until the given sweep call ran.
        /// </summary>
        /// <param name="calls">How many sweep calls to wait for.</param>
        /// <param name="cancellationToken">The token used to cancel the wait.</param>
        /// <returns>The age the awaited sweep call swept with.</returns>
        private async Task<TimeSpan> WaitForCountAsync(int calls, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task<TimeSpan> waiting;

                lock (_lock)
                {
                    if (_ages.Count >= calls)
                    {
                        return _ages[calls - 1];
                    }

                    _nextSweep ??= new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
                    waiting = _nextSweep.Task;
                }

                await waiting.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     A logger that counts error entries.
    /// </summary>
    /// <typeparam name="TCategory">The logged category.</typeparam>
    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        /// <summary>
        ///     Guards the error count below.
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        ///     How many error entries were logged.
        /// </summary>
        private int _errors;

        /// <summary>
        ///     Gets how many error entries were logged.
        /// </summary>
        public int Errors
        {
            get
            {
                lock (_lock)
                {
                    return _errors;
                }
            }
        }

        /// <inheritdoc />
        IDisposable? ILogger.BeginScope<TState>(TState state)
        {
            return null;
        }

        /// <inheritdoc />
        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <inheritdoc />
        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                lock (_lock)
                {
                    _errors++;
                }
            }
        }
    }
}
