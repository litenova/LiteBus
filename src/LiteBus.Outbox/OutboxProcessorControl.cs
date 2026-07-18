using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Gates the outbox processor background loop for pause, resume, and drain operations.
/// </summary>
public sealed class OutboxProcessorControl : IOutboxProcessorControl, IProcessorBackgroundControl, IAsyncDisposable
{
    /// <summary>
    ///     Signals that the drain pass has completed and the loop has exited.
    /// </summary>
    private readonly TaskCompletionSource _drainComplete =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Cancels the processor polling delay when drain starts.
    /// </summary>
    private readonly CancellationTokenSource _drainRequested = new();

    /// <summary>
    ///     Protects processor state and pass lifecycle transitions.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     Completes when the active processing pass finishes.
    /// </summary>
    private TaskCompletionSource _passComplete = CreateCompletedSignal();

    /// <summary>
    ///     Completes when a paused loop may resume entering passes.
    /// </summary>
    private TaskCompletionSource _resume = CreateCompletedSignal();

    /// <summary>
    ///     Indicates whether a drain operation requested loop termination after one pass.
    /// </summary>
    private volatile bool _drainSignalled;

    /// <summary>
    ///     Indicates whether the processor loop currently owns an active pass.
    /// </summary>
    private bool _passActive;

    /// <summary>
    ///     The current processor loop state.
    /// </summary>
    private volatile ProcessorState _state = ProcessorState.Running;

    /// <summary>
    ///     Gets a value indicating whether the loop should perform one final pass and exit.
    /// </summary>
    /// <value><see langword="true" /> when <see cref="DrainAsync" /> has been requested; otherwise <see langword="false" />.</value>
    internal bool IsDraining => _drainSignalled;

    /// <inheritdoc />
    CancellationToken IProcessorBackgroundControl.DrainRequestedToken => _drainRequested.Token;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _drainRequested.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ProcessorState State => _state;

    /// <inheritdoc />
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task passComplete;

        lock (_sync)
        {
            ThrowIfDraining();

            if (_state == ProcessorState.Running)
            {
                _state = ProcessorState.Paused;
                _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            passComplete = _passComplete.Task;
        }

        await passComplete.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            ThrowIfDraining();

            if (_state == ProcessorState.Paused)
            {
                _state = ProcessorState.Running;
                _resume.TrySetResult();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var signalDrain = false;

        lock (_sync)
        {
            if (!_drainSignalled)
            {
                _state = ProcessorState.Draining;
                _drainSignalled = true;
                signalDrain = true;
                _resume.TrySetResult();
            }
        }

        if (signalDrain)
        {
            await _drainRequested.CancelAsync().ConfigureAwait(false);
        }

        await _drainComplete.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    Task IProcessorBackgroundControl.WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        return WaitIfPausedAsync(cancellationToken);
    }

    /// <inheritdoc />
    bool IProcessorBackgroundControl.IsDraining => IsDraining;

    /// <inheritdoc />
    void IProcessorBackgroundControl.SignalPassComplete()
    {
        SignalPassComplete();
    }

    /// <inheritdoc />
    void IProcessorBackgroundControl.SignalDrainComplete(Exception? exception)
    {
        SignalDrainComplete(exception);
    }

    /// <summary>
    ///     Waits until the processor loop may enter the next pass.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel waiting for the gate.</param>
    /// <returns>A task that completes when the loop may proceed.</returns>
    internal async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task resume;

            lock (_sync)
            {
                if (_state != ProcessorState.Paused)
                {
                    if (_passActive)
                    {
                        throw new InvalidOperationException("The outbox processor loop attempted to start overlapping passes.");
                    }

                    _passActive = true;
                    _passComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    return;
                }

                resume = _resume.Task;
            }

            await resume.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Signals that the active processor pass has completed.
    /// </summary>
    internal void SignalPassComplete()
    {
        lock (_sync)
        {
            if (!_passActive)
            {
                return;
            }

            _passActive = false;
            _passComplete.TrySetResult();
        }
    }

    /// <summary>
    ///     Signals that the processor loop exited after completing or failing the drain pass.
    /// </summary>
    /// <param name="exception">The failure that ended the drain pass, or <see langword="null" /> when it succeeded.</param>
    internal void SignalDrainComplete(Exception? exception = null)
    {
        if (exception is null)
        {
            _drainComplete.TrySetResult();
            return;
        }

        _drainComplete.TrySetException(exception);
    }

    /// <summary>
    ///     Rejects pause and resume transitions after drain has started because the loop is terminating.
    /// </summary>
    private void ThrowIfDraining()
    {
        if (_drainSignalled)
        {
            throw new InvalidOperationException("The outbox processor cannot pause or resume after drain has started.");
        }
    }

    /// <summary>
    ///     Creates an asynchronously continued signal in the completed state.
    /// </summary>
    /// <returns>A completed signal.</returns>
    private static TaskCompletionSource CreateCompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }
}
