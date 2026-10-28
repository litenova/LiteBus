using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Outbox dispatcher double that fails scripted contract names while delegating the rest to an inner dispatcher.
/// </summary>
/// <remarks>
///     <para>
///         Scripts are keyed by envelope contract name using ordinal comparison. The same exception instance is thrown
///         on each scripted failure, so assert on exception type and message rather than stack trace or identity.
///     </para>
///     <para>
///         Cancellation is honored before any script runs: a canceled token throws
///         <see cref="OperationCanceledException" /> without consuming a scripted failure, recording an attempt, or
///         calling the inner dispatcher. All members are thread-safe.
///     </para>
/// </remarks>
public sealed class ScriptedOutboxDispatcher : IOutboxDispatcher
{
    /// <summary>
    ///     Gets the inner dispatcher that receives dispatches the scripts do not fail, or <see langword="null" /> when dispatches succeed without delegation.
    /// </summary>
    private readonly IOutboxDispatcher? _inner;

    /// <summary>
    ///     Gets the envelopes observed by <see cref="DispatchAsync" /> in call order.
    /// </summary>
    private readonly List<OutboxEnvelope> _attempted = [];

    /// <summary>
    ///     Gets the envelopes that passed the scripts and completed dispatch in call order.
    /// </summary>
    private readonly List<OutboxEnvelope> _dispatched = [];

    /// <summary>
    ///     Gets the dispatch counts keyed by contract name using ordinal comparison.
    /// </summary>
    private readonly Dictionary<string, int> _attemptCounts = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the failure scripts keyed by contract name using ordinal comparison.
    /// </summary>
    private readonly Dictionary<string, DispatchScript> _scripts = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the lock that serializes script and recording mutations.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="ScriptedOutboxDispatcher" /> class.
    /// </summary>
    /// <param name="inner">The dispatcher that receives dispatches the scripts do not fail, or <see langword="null" /> to record successes without delegation.</param>
    public ScriptedOutboxDispatcher(IOutboxDispatcher? inner = null)
    {
        _inner = inner;
    }

    /// <summary>
    ///     Gets the envelopes observed by <see cref="DispatchAsync" /> in call order, including scripted failures.
    /// </summary>
    public IReadOnlyList<OutboxEnvelope> AttemptedEnvelopes
    {
        get
        {
            lock (_sync)
            {
                return [.. _attempted];
            }
        }
    }

    /// <summary>
    ///     Gets the envelopes that passed the scripts and completed dispatch in call order.
    /// </summary>
    public IReadOnlyList<OutboxEnvelope> DispatchedEnvelopes
    {
        get
        {
            lock (_sync)
            {
                return [.. _dispatched];
            }
        }
    }

    /// <inheritdoc />
    public async Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        Exception? scriptedFailure;

        lock (_sync)
        {
            _attempted.Add(message);
            _attemptCounts[message.ContractName] = _attemptCounts.TryGetValue(message.ContractName, out var count)
                ? count + 1
                : 1;
            scriptedFailure = TakeScriptedFailure(message.ContractName);
        }

        if (scriptedFailure is not null)
        {
            throw scriptedFailure;
        }

        if (_inner is not null)
        {
            await _inner.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _dispatched.Add(message);
        }
    }

    /// <summary>
    ///     Fails the next <paramref name="times" /> dispatches of one contract name with the supplied error.
    /// </summary>
    /// <param name="contractName">The envelope contract name the script applies to.</param>
    /// <param name="error">The exception instance thrown on each scripted failure.</param>
    /// <param name="times">The number of dispatches to fail before the script expires.</param>
    /// <remarks>
    ///     A new script for the same contract name replaces the previous one, including an always-fail script.
    /// </remarks>
    public void FailNext(string contractName, Exception error, int times = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(times, 0);

        lock (_sync)
        {
            _scripts[contractName] = new DispatchScript(error, times);
        }
    }

    /// <summary>
    ///     Fails every dispatch of one contract name with the supplied error until the script is cleared or replaced.
    /// </summary>
    /// <param name="contractName">The envelope contract name the script applies to.</param>
    /// <param name="error">The exception instance thrown on each scripted failure.</param>
    public void FailAlways(string contractName, Exception error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentNullException.ThrowIfNull(error);

        lock (_sync)
        {
            _scripts[contractName] = DispatchScript.Always(error);
        }
    }

    /// <summary>
    ///     Gets the number of dispatches observed for one contract name, including scripted failures.
    /// </summary>
    /// <param name="contractName">The envelope contract name to count.</param>
    /// <returns>The observed dispatch count, or zero when the contract was never dispatched.</returns>
    public int GetAttemptCount(string contractName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        lock (_sync)
        {
            return _attemptCounts.TryGetValue(contractName, out var count) ? count : 0;
        }
    }

    /// <summary>
    ///     Removes the failure script for one contract name without touching recordings or attempt counts.
    /// </summary>
    /// <param name="contractName">The envelope contract name whose script is removed.</param>
    /// <returns><see langword="true" /> when a script was removed; otherwise <see langword="false" />.</returns>
    public bool ClearContract(string contractName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        lock (_sync)
        {
            return _scripts.Remove(contractName);
        }
    }

    /// <summary>
    ///     Removes every failure script and clears recorded envelopes and attempt counts.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _scripts.Clear();
            _attempted.Clear();
            _dispatched.Clear();
            _attemptCounts.Clear();
        }
    }

    /// <summary>
    ///     Consumes one scripted failure for a contract name when a live script applies.
    /// </summary>
    /// <param name="contractName">The envelope contract name to evaluate.</param>
    /// <returns>The scripted exception, or <see langword="null" /> when the dispatch may proceed.</returns>
    /// <remarks>
    ///     Callers must hold the mutation lock; the script state mutates in place.
    /// </remarks>
    private Exception? TakeScriptedFailure(string contractName)
    {
        if (!_scripts.TryGetValue(contractName, out var script))
        {
            return null;
        }

        if (script.AlwaysFail)
        {
            return script.Error;
        }

        if (script.RemainingFailures == 0)
        {
            return null;
        }

        script.RemainingFailures--;
        return script.Error;
    }

    /// <summary>
    ///     Holds one contract failure script and its remaining failure budget.
    /// </summary>
    private sealed class DispatchScript
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DispatchScript" /> class.
        /// </summary>
        /// <param name="error">The exception instance thrown on each scripted failure.</param>
        /// <param name="remainingFailures">The number of failures left before the script expires.</param>
        /// <param name="alwaysFail">Whether the script fails every dispatch without expiring.</param>
        private DispatchScript(Exception error, int remainingFailures, bool alwaysFail)
        {
            Error = error;
            RemainingFailures = remainingFailures;
            AlwaysFail = alwaysFail;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DispatchScript" /> class that expires after a fixed number of failures.
        /// </summary>
        /// <param name="error">The exception instance thrown on each scripted failure.</param>
        /// <param name="remainingFailures">The number of failures left before the script expires.</param>
        public DispatchScript(Exception error, int remainingFailures)
            : this(error, remainingFailures, alwaysFail: false)
        {
        }

        /// <summary>
        ///     Gets the exception instance thrown on each scripted failure.
        /// </summary>
        public Exception Error { get; }

        /// <summary>
        ///     Gets or sets the number of failures left before the script expires.
        /// </summary>
        public int RemainingFailures { get; set; }

        /// <summary>
        ///     Gets a value indicating whether the script fails every dispatch without expiring.
        /// </summary>
        public bool AlwaysFail { get; }

        /// <summary>
        ///     Creates a script that fails every dispatch without expiring.
        /// </summary>
        /// <param name="error">The exception instance thrown on each scripted failure.</param>
        /// <returns>The always-fail script.</returns>
        public static DispatchScript Always(Exception error)
        {
            return new DispatchScript(error, remainingFailures: 0, alwaysFail: true);
        }
    }
}
