using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Hosting;

/// <summary>
///     Default implementation of <see cref="ICommandInboxProcessorHost" />.
/// </summary>
internal sealed class CommandInboxProcessorHost : ICommandInboxProcessorHost
{
    private readonly CommandInboxProcessorHostOptions _hostOptions;
    private readonly IEnumerable<ICommandInboxProcessorHostLifecycle> _lifecycles;
    private readonly Abstractions.ICommandInboxProcessor _processor;
    private readonly CommandInboxProcessorOptions _processorOptions;
    private readonly CommandInboxProcessorHostState _state;
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandInboxProcessorHost" /> class.
    /// </summary>
    /// <param name="processor">The inbox processor that performs each pass.</param>
    /// <param name="processorOptions">The batch and lease options used to interpret adaptive polling.</param>
    /// <param name="hostOptions">The loop timing and adaptive polling options.</param>
    /// <param name="state">The mutable host state published to health checks.</param>
    /// <param name="clock">The time provider used to record pass timestamps.</param>
    /// <param name="lifecycles">Optional lifecycle hooks invoked at host start and stop.</param>
    public CommandInboxProcessorHost(
        Abstractions.ICommandInboxProcessor processor,
        CommandInboxProcessorOptions processorOptions,
        CommandInboxProcessorHostOptions hostOptions,
        CommandInboxProcessorHostState state,
        TimeProvider clock,
        IEnumerable<ICommandInboxProcessorHostLifecycle>? lifecycles = null)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _processorOptions = processorOptions ?? throw new ArgumentNullException(nameof(processorOptions));
        _hostOptions = hostOptions ?? throw new ArgumentNullException(nameof(hostOptions));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _lifecycles = lifecycles ?? [];
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_hostOptions.Enabled)
        {
            return;
        }

        if (_hostOptions.StartupDelay > TimeSpan.Zero)
        {
            await Task.Delay(_hostOptions.StartupDelay, cancellationToken).ConfigureAwait(false);
        }

        await InvokeLifecycleAsync(static (lifecycle, token) => lifecycle.OnHostStartingAsync(token), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var passResult = await _processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
                    _state.RecordSuccessfulPass(_clock.GetUtcNow());

                    if (ShouldDelayAfterPass(passResult))
                    {
                        await Task.Delay(_hostOptions.PollInterval, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _state.RecordFailure(_clock.GetUtcNow(), MessageProcessorDiagnostics.FormatError(exception));
                    await Task.Delay(_hostOptions.PollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await InvokeLifecycleAsync(static (lifecycle, token) => lifecycle.OnHostStoppingAsync(token), CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Determines whether the host should wait before the next pass.
    /// </summary>
    /// <param name="passResult">The result from the pass that just completed.</param>
    /// <returns><see langword="true" /> when a poll delay should be applied; otherwise, <see langword="false" />.</returns>
    private bool ShouldDelayAfterPass(ProcessorPassResult passResult)
    {
        if (_hostOptions.PollInterval <= TimeSpan.Zero)
        {
            return false;
        }

        if (_hostOptions.UseAdaptivePolling && passResult.LeasedCount >= _processorOptions.BatchSize)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Invokes a lifecycle callback on every registered lifecycle participant.
    /// </summary>
    /// <param name="callback">The lifecycle callback to invoke.</param>
    /// <param name="cancellationToken">A token used to cancel lifecycle work.</param>
    private async Task InvokeLifecycleAsync(
        Func<ICommandInboxProcessorHostLifecycle, CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        foreach (var lifecycle in _lifecycles)
        {
            await callback(lifecycle, cancellationToken).ConfigureAwait(false);
        }
    }
}
