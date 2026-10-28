using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     What one harness run did: how it ended, and which stages ran to get there.
/// </summary>
/// <remarks>
///     <see cref="StagesRun" /> is the part a consumer cannot build. When the point of the library is that behavior
///     moved into named stages, asserting which stages ran is what a test of that behavior wants, and only the stage
///     runner knows it.
/// </remarks>
public sealed record MediationHarnessResult
{
    /// <summary>
    ///     Gets how the mediation ended.
    /// </summary>
    public required MediationOutcome Outcome { get; init; }

    /// <summary>
    ///     Gets the pre stages that ran, in the order they ran.
    /// </summary>
    /// <value>
    ///     Only the stages that had a handler registered. A stage with nothing in it is skipped and does not appear,
    ///     which is what makes the sequence readable as "this is what happened".
    /// </value>
    public required IReadOnlyList<PreStage> StagesRun { get; init; }

    /// <summary>
    ///     Gets why the pipeline stopped, when a decision stopped it.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     Gets the machine-readable code the decision supplied.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    ///     Gets the failures the validator stage collected.
    /// </summary>
    public IReadOnlyList<ValidationFailure> Failures { get; init; } = [];

    /// <summary>
    ///     Gets the value the mediation produced, when the message produces one and the pipeline reached it.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the main handler ran.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> for a denial, a validation failure, and an answered message. Separate from the
    ///     outcome because it is the assertion a test of a guard actually wants to make.
    /// </value>
    public required bool MainHandlerRan { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the message was handled or answered.
    /// </summary>
    public bool IsSuccess => Outcome is MediationOutcome.Succeeded or MediationOutcome.Answered;

    /// <summary>
    ///     Gets a value indicating whether a guard refused the message.
    /// </summary>
    public bool IsDenied => Outcome == MediationOutcome.Denied;

    /// <summary>
    ///     Gets a value indicating whether a validator reported the message malformed.
    /// </summary>
    public bool IsInvalid => Outcome == MediationOutcome.Invalid;
}
