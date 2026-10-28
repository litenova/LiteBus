using System;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     One "every message must state a position on this" rule, and the messages it applies to.
/// </summary>
/// <param name="ValueType">The metadata value type each message in scope must declare or record an exemption from.</param>
/// <param name="Scope">
///     The predicate deciding whether a message type is in scope, or <see langword="null" /> when the requirement
///     applies to every registered message.
/// </param>
/// <param name="ScopeDescription">
///     What the scope is, in the words the composition error uses. "every command" and
///     "every command implementing IActingAccountCommand" read as a policy; a predicate cannot describe itself.
/// </param>
/// <remarks>
///     <para>
///         An unscoped requirement was the whole of this feature until now, and it made the mechanism work against
///         itself. Requiring a permission declaration on commands also demanded one from every query, so an
///         application wrote one exemption per query saying nothing, which trains a team to treat rationales as
///         paperwork. An exemption is supposed to be a decision.
///     </para>
///     <para>
///         The predicate form is where the feature earns its keep: "every command that names an acting account must
///         declare what that account has to be permitted to do" is a rule a security review can read, and it is
///         enforced against commands written after the review.
///     </para>
/// </remarks>
internal sealed record DeclarationRequirement(
    Type ValueType,
    Func<Type, bool>? Scope,
    string ScopeDescription)
{
    /// <summary>
    ///     Determines whether a registered message type is in this requirement's scope.
    /// </summary>
    /// <param name="messageType">The concrete registered message type.</param>
    /// <returns><see langword="true" /> when the message has to state a position.</returns>
    public bool Covers(Type messageType)
    {
        return Scope is null || Scope(messageType);
    }
}
