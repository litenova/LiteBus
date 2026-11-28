using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The nested mediation a <see cref="IMediationNestingRule" /> judges: the enclosing context and the inner message type.
/// </summary>
/// <param name="Parent">The execution context of the enclosing mediation.</param>
/// <param name="MessageType">The runtime type of the message the nested mediation handles.</param>
public sealed record MediationNestingContext(IExecutionContext Parent, Type MessageType)
{
    /// <summary>
    ///     Walks the enclosing mediations from the direct parent up to the root.
    /// </summary>
    /// <returns>The enclosing contexts, nearest first.</returns>
    public IEnumerable<IExecutionContext> Ancestors()
    {
        IExecutionContext? current = Parent;

        while (current is not null)
        {
            yield return current;
            current = current.Parent;
        }
    }
}
