using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LiteBus.Messaging;

/// <summary>
///     What the host actually composed: how many messages of each axis, which open generic handlers were closed over
///     how many of them, which handler types cover one message's stage twice, whether auditing and idempotency are on,
///     and which declaration policies are enforced.
/// </summary>
/// <remarks>
///     <para>
///         Resolve it from the container and log <see cref="ToString" /> once at startup. One line catches a great deal:
///         a trail registered with no axis enabled, an axis enabled with no messages, a generic guard that closed over
///         every command when the author meant one, and a required declaration nobody satisfied.
///     </para>
///     <para>
///         The open generic line is the one that earns it. A handler discovered by assembly scanning inserts a pipeline
///         stage into every message it fits, with no registration line to review, so a reviewer cannot see it from the
///         composition code. Here it is a number that changes when the set changes.
///     </para>
///     <para>
///         It is filled in after every module has built, because the counts are not known until then. Resolving it
///         during composition returns an empty summary.
///     </para>
/// </remarks>
public sealed class LiteBusCompositionSummary
{
    /// <summary>
    ///     Message counts by axis name, in the order the axis modules recorded them.
    /// </summary>
    private readonly Dictionary<string, int> _messageCountsByAxis = new(StringComparer.Ordinal);

    /// <summary>
    ///     Each open generic handler and the number of message types it was closed over.
    /// </summary>
    private readonly List<OpenGenericClosure> _openGenericHandlers = [];

    /// <summary>
    ///     The declaration policies the composition enforces, rendered for a reader.
    /// </summary>
    private readonly List<string> _requiredDeclarations = [];

    /// <summary>
    ///     The safeguards the application turned off on purpose, each with the reason it gave, in recording order.
    /// </summary>
    private readonly List<string> _relaxedSafeguards = [];

    /// <summary>
    ///     Each handler type covering one message's stage twice, once directly and once through a base type.
    /// </summary>
    private readonly List<DuplicateStageCoverage> _duplicateStageCoverages = [];

    /// <summary>
    ///     Gets the total number of registered messages.
    /// </summary>
    public int MessageCount { get; internal set; }

    /// <summary>
    ///     Gets the message counts by axis name, such as <c>commands</c> and <c>queries</c>.
    /// </summary>
    /// <value>
    ///     One entry per axis module that was registered. An axis the host does not compose is absent rather than
    ///     zero, so the summary says what was wired rather than what was not.
    /// </value>
    public IReadOnlyDictionary<string, int> MessageCountsByAxis => _messageCountsByAxis;

    /// <summary>
    ///     Gets each open generic handler and the messages it was closed over.
    /// </summary>
    public IReadOnlyList<OpenGenericClosure> OpenGenericHandlers => _openGenericHandlers;

    /// <summary>
    ///     Gets the declaration policies the composition enforces.
    /// </summary>
    public IReadOnlyList<string> RequiredDeclarations => _requiredDeclarations;

    /// <summary>
    ///     Gets each handler type covering one message's stage twice, once directly and once through a base type.
    /// </summary>
    /// <value>
    ///     One entry per handler type, message, and role where both sets hold an entry, so the handler runs twice.
    ///     Empty when every stage holds at most one entry per handler type, which is the ordinary composition.
    /// </value>
    public IReadOnlyList<DuplicateStageCoverage> DuplicateStageCoverages => _duplicateStageCoverages;

    /// <summary>
    ///     Gets the number of application composition checks the host runs.
    /// </summary>
    public int CompositionChecks { get; internal set; }

    /// <summary>
    ///     Gets the safeguards the application turned off on purpose, such as publishing outbox envelopes with no
    ///     handler, each with the reason it gave.
    /// </summary>
    public IReadOnlyList<string> RelaxedSafeguards => _relaxedSafeguards;

    /// <summary>
    ///     Records a safeguard the application turned off on purpose, so the startup summary names it.
    /// </summary>
    /// <param name="description">What was turned off and the reason the application gave.</param>
    /// <remarks>Called by LiteBus modules in other assemblies during composition; not for application code.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void RecordRelaxedSafeguard(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _relaxedSafeguards.Add(description);
    }

    /// <summary>
    ///     Renders the summary as the startup line an operator reads.
    /// </summary>
    /// <returns>The rendered summary.</returns>
    public override string ToString()
    {
        var report = new StringBuilder("LiteBus composed ");
        report.Append(MessageCount.ToString(CultureInfo.InvariantCulture)).Append(" messages");

        if (_messageCountsByAxis.Count > 0)
        {
            var axes = _messageCountsByAxis
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{pair.Value.ToString(CultureInfo.InvariantCulture)} {pair.Key}");

            report.Append(" (").Append(string.Join(", ", axes)).Append(')');
        }

        foreach (var closure in _openGenericHandlers.OrderBy(static handler => handler.HandlerName, StringComparer.Ordinal))
        {
            report.Append("; open generic ")
                  .Append(closure.HandlerName)
                  .Append(" closed over ")
                  .Append(closure.MessageCount.ToString(CultureInfo.InvariantCulture))
                  .Append(" messages");
        }

        foreach (var duplicate in _duplicateStageCoverages
                     .OrderBy(static entry => entry.HandlerType.Name, StringComparer.Ordinal)
                     .ThenBy(static entry => entry.MessageType.Name, StringComparer.Ordinal))
        {
            report.Append("; duplicate stage coverage ")
                  .Append(duplicate.HandlerType.Name)
                  .Append(" on ")
                  .Append(duplicate.MessageType.Name)
                  .Append(" (direct ")
                  .Append(duplicate.DirectContractType.Name)
                  .Append(", indirect ")
                  .Append(duplicate.IndirectContractType.Name)
                  .Append(')');
        }

        if (_requiredDeclarations.Count > 0)
        {
            report.Append("; required declarations ").Append(string.Join(", ", _requiredDeclarations));
        }

        if (CompositionChecks > 0)
        {
            report.Append("; ")
                  .Append(CompositionChecks.ToString(CultureInfo.InvariantCulture))
                  .Append(" composition checks");
        }

        foreach (var safeguard in _relaxedSafeguards)
        {
            report.Append("; relaxed safeguard: ").Append(safeguard);
        }

        return report.ToString();
    }

    /// <summary>
    ///     Records how many messages one axis registered.
    /// </summary>
    /// <param name="axis">The axis name, such as <c>commands</c>.</param>
    /// <param name="count">The number of messages that axis registered.</param>
    internal void RecordAxis(string axis, int count)
    {
        _messageCountsByAxis[axis] = count;
    }

    /// <summary>
    ///     Records one open generic handler and the messages it was closed over.
    /// </summary>
    /// <param name="handlerName">The handler type name.</param>
    /// <param name="messageCount">The number of message types it was closed over.</param>
    internal void RecordOpenGeneric(string handlerName, int messageCount)
    {
        _openGenericHandlers.Add(new OpenGenericClosure(handlerName, messageCount));
    }

    /// <summary>
    ///     Records one declaration policy the composition enforces.
    /// </summary>
    /// <param name="description">The policy, rendered for a reader.</param>
    internal void RecordRequiredDeclaration(string description)
    {
        _requiredDeclarations.Add(description);
    }

    /// <summary>
    ///     Records one handler type covering one message's stage twice.
    /// </summary>
    /// <param name="handlerType">The open generic handler definition or closed handler type covering both sets.</param>
    /// <param name="messageType">The registered message covered twice.</param>
    /// <param name="directContractType">The contract closed over the message itself.</param>
    /// <param name="indirectContractType">The contract closed over a base type of the message.</param>
    internal void RecordDuplicateStageCoverage(Type handlerType, Type messageType, Type directContractType, Type indirectContractType)
    {
        _duplicateStageCoverages.Add(new DuplicateStageCoverage(handlerType, messageType, directContractType, indirectContractType));
    }
}
