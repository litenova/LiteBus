using System.Collections.Concurrent;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests;

/// <summary>
///     Collects the completion contexts observed during one test run.
/// </summary>
/// <remarks>
///     Shared by the pipeline and completion suites, so it sits in the root test namespace that both already import
///     rather than in whichever folder happened to need it first.
/// </remarks>
internal sealed class CompletionRecorder
{
    /// <summary>
    ///     Gets the completion contexts observed, in the order the handlers ran.
    /// </summary>
    public ConcurrentQueue<(string Handler, MessageCompletionContext Context)> Observed { get; } = new();

    /// <summary>
    ///     Gets the typed results observed by completion handlers that ask for the result type.
    /// </summary>
    public ConcurrentQueue<(bool HasResult, string? Result)> TypedResults { get; } = new();
}
