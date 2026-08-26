namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Identifies which of the three decision stages a pre-handler belongs to.
/// </summary>
/// <remarks>
///     <para>
///         The stages run in the order the members are declared, and the order is fixed by the framework rather than by
///         handler priority. That is what makes "authorization runs before a cached answer is served" a guarantee instead
///         of a convention every application has to rediscover: a shortcut cannot answer a caller that a guard has not
///         yet allowed, whatever priorities are written and whether the handlers are registered directly or globally.
///     </para>
///     <para>
///         Within one stage the existing rules still apply. Indirect handlers run before direct ones, handlers run in
///         ascending <see cref="HandlerPriorityAttribute" /> order, and the first stopping decision ends the stage.
///     </para>
/// </remarks>
public enum PipelineStage
{
    /// <summary>
    ///     The stage that decides whether the message is permitted to proceed at all.
    /// </summary>
    /// <remarks>
    ///     Guards run first and in full. Nothing else in the pipeline observes the message until every guard has allowed
    ///     it, which is the ordering guarantee the split exists to provide.
    /// </remarks>
    Guard = 0,

    /// <summary>
    ///     The stage that decides whether the answer is already known, so the main handler would add nothing.
    /// </summary>
    /// <remarks>
    ///     A cache hit and an idempotent message that detects it already took effect are the two cases. Shortcuts run
    ///     after guards and before pre-handlers, so an answer never reaches a caller a guard would have refused, and a
    ///     shortcut does not pay for validation and enrichment it is about to skip.
    /// </remarks>
    Shortcut = 1,

    /// <summary>
    ///     The stage that validates, enriches, or otherwise prepares a message that is going to be handled.
    /// </summary>
    /// <remarks>
    ///     A pre-handler cannot stop the pipeline by returning. Stopping is a capability, and it belongs to the two
    ///     stages whose whole purpose is to decide.
    /// </remarks>
    PreHandler = 2
}
