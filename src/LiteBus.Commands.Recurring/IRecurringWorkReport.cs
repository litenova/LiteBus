namespace LiteBus.Commands.Recurring;

/// <summary>
///     Lets the result of a recurring command tell the loop whether work remains.
/// </summary>
/// <remarks>
///     <para>
///         A command that drains a batch usually knows whether it filled the batch, and that is the difference between
///         a backlog cleared in one interval and a backlog cleared in a hundred. A result implementing this interface
///         makes the loop send again immediately instead of waiting out
///         <see cref="RecurringCommandHostOptions.Interval" />.
///     </para>
///     <para>
///         Implementing it is optional. A command with no result, or one whose result does not implement this, waits
///         the full interval between sends, which is the safe default.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed record SendDueRemindersResult(int Sent, int BatchSize) : IRecurringWorkReport
/// {
///     public bool HasMoreWork => Sent >= BatchSize;
/// }
/// ]]></code>
/// </example>
public interface IRecurringWorkReport
{
    /// <summary>
    ///     Gets a value indicating whether the loop should send again without waiting.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> when the pass did as much as it was allowed to and more is waiting, which normally
    ///     means the batch came back full.
    /// </value>
    bool HasMoreWork { get; }
}
