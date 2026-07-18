namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Describes the operational state of an inbox processor background loop.
/// </summary>
public enum ProcessorState
{
    /// <summary>
    ///     The processor loop is actively leasing and dispatching messages.
    /// </summary>
    Running = 0,

    /// <summary>
    ///     The processor loop is suspended and does not start new passes.
    /// </summary>
    Paused = 1,

    /// <summary>
    ///     The processor loop is finishing one final pass before stopping.
    /// </summary>
    Draining = 2
}