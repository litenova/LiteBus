using System;
using System.ComponentModel;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Answers whether a handler implements a messaging-level pipeline contract over messages of one axis, so an axis
///     builder can accept a handler written once for both.
/// </summary>
/// <remarks>
///     <para>
///         An axis builder recognises its own contracts, <c>ICommandGuard&lt;TCommand&gt;</c> and its siblings, and
///         used to refuse a handler that implemented only the messaging-level <c>IMessageGuard&lt;TMessage&gt;</c>.
///         That forced a cross-cutting guard to be written twice, once per axis, and the thing consumers were copying
///         was authorization code, where two copies means one of them gets the fix.
///     </para>
///     <para>
///         Registering such a handler on the messaging module instead does work, and is not good enough: it closes
///         over every registered message including events, and a guard written for messages that carry an acting
///         account has no business running on domain facts. Accepting it per axis is what lets the author say which
///         axes it covers.
///     </para>
///     <para>
///         The scope is read from the constraint rather than assumed. <c>AuthorizationGuard&lt;TMessage&gt; where
///         TMessage : ICommand</c> is a command construct; the same class constrained to <c>IQuery</c> is a query
///         construct; one constrained to neither is refused, because it would silently close over every message in
///         whichever axis happened to register it.
///     </para>
///     <para>
///         Main handler contracts are deliberately not included. A command handler and a query handler mean different
///         things, and one class answering both is a modelling error rather than a cross-cutting concern.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MessagingHandlerContracts
{
    /// <summary>
    ///     Determines whether a closed interface is a messaging-level pipeline contract naming messages of one axis.
    /// </summary>
    /// <param name="contract">One interface from a handler type's interface list.</param>
    /// <param name="messageMarker">The axis message contract, such as <c>ICommand</c>.</param>
    /// <returns>
    ///     <see langword="true" /> when the handler is a pipeline handler whose message type is, or is constrained to
    ///     be, assignable to <paramref name="messageMarker" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="contract" /> or <paramref name="messageMarker" /> is <see langword="null" />.
    /// </exception>
    public static bool NamesMessageAssignableTo(Type contract, Type messageMarker)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(messageMarker);

        if (!contract.IsGenericType || !PipelineContracts.IsDispatchable(contract.GetGenericTypeDefinition()))
        {
            return false;
        }

        var messageType = contract.GetGenericArguments()[0];

        if (!messageType.IsGenericParameter)
        {
            return messageMarker.IsAssignableFrom(messageType);
        }

        foreach (var constraint in messageType.GetGenericParameterConstraints())
        {
            if (messageMarker.IsAssignableFrom(constraint))
            {
                return true;
            }
        }

        return false;
    }
}
