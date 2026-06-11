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
    private readonly SemaphoreSlim _drainComplete = new(0, 1);

    /// <summary>
    ///     Serializes loop entry; pause holds the gate without releasing it.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    ///     Indicates whether a drain operation requested loop termination after one pass.
    /// </summary>
    private volatile bool _drainSignalled;

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
    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        _drainComplete.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ProcessorState State => _state;

    /// <inheritdoc />
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _state = ProcessorState.Paused;
    }

    /// <inheritdoc />
    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_state != ProcessorState.Paused)
        {
            return Task.CompletedTask;
        }

        _state = ProcessorState.Running;
        _gate.Release();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _state = ProcessorState.Draining;
        _drainSignalled = true;

        if (_gate.CurrentCount == 0)
        {
            _gate.Release();
        }

        await _drainComplete.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    Task IProcessorBackgroundControl.WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        return WaitIfPausedAsync(cancellationToken);
    }

    /// <inheritdoc />
    bool IProcessorBackgroundControl.IsDraining => IsDraining;

    /// <inheritdoc />
    void IProcessorBackgroundControl.SignalDrainComplete()
    {
        SignalDrainComplete();
    }

    /// <summary>
    ///     Waits until the processor loop may enter the next pass.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel waiting for the gate.</param>
    /// <returns>A task that completes when the loop may proceed.</returns>
    internal async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _gate.Release();
    }

    /// <summary>
    ///     Signals that the processor loop exited after completing the drain pass.
    /// </summary>
    internal void SignalDrainComplete()
    {
        _drainComplete.Release();
    }
}