using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     A read-only view of every registered message and what it declares, for a composition check to assert over.
/// </summary>
/// <remarks>
///     <para>
///         Handed to the callback passed to <c>MessageModuleBuilder.ValidateComposition</c>, after every module has
///         built, so it holds every message the host composed. Applications use it to enforce their own conventions:
///         that audit action codes are unique, that they follow the house naming rule, that a family of commands all
///         declare the same value.
///     </para>
///     <para>
///         It exists because those checks were previously unreachable. The hook underneath is public, but the only way
///         to be handed a module configuration is to implement a module, which is a lot of ceremony for a five-line
///         assertion, so consumers concluded the capability did not exist.
///     </para>
/// </remarks>
public interface IMessageCatalog : IEnumerable<MessageCatalogEntry>
{
    /// <summary>
    ///     Gets the number of registered messages in the catalog.
    /// </summary>
    int Count { get; }

    /// <summary>
    ///     Gets every message in scope that states no position on a metadata value type.
    /// </summary>
    /// <param name="valueType">The metadata value type each message in scope must declare or record an exemption from.</param>
    /// <param name="scope">
    ///     The predicate deciding whether a message is in scope, or <see langword="null" /> for every message.
    /// </param>
    /// <returns>
    ///     One entry per concrete message in scope that neither declares the value nor records an exemption from it,
    ///     in registration order.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="valueType" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         This is what the <c>litebus.declarations</c> probe reads: a requirement enforced as
    ///         <see cref="DeclarationEnforcement.Report" /> lists its offenders through here instead of failing
    ///         composition, so a team adopts positions message by message.
    ///     </para>
    ///     <para>
    ///         Abstract types and interfaces are skipped: they are shapes rather than messages, which is the same rule
    ///         the declaration requirements apply.
    ///     </para>
    /// </remarks>
    IReadOnlyList<MessageCatalogEntry> MissingDeclaration(Type valueType, Func<Type, bool>? scope = null)
    {
        ArgumentNullException.ThrowIfNull(valueType);

        List<MessageCatalogEntry> missing = [];

        foreach (var entry in this)
        {
            if (entry.MessageType.IsAbstract || entry.MessageType.IsInterface)
            {
                continue;
            }

            if (scope is not null && !scope(entry.MessageType))
            {
                continue;
            }

            entry.Metadata.TryGet<DeclarationExemptions>(out var exemptions);

            if (DeclaresValue(entry.Metadata, valueType) || exemptions?.Covers(valueType) == true)
            {
                continue;
            }

            missing.Add(entry);
        }

        return missing;
    }

    /// <summary>
    ///     Determines whether the metadata holds a value of the required type.
    /// </summary>
    /// <param name="metadata">The metadata resolved for one message.</param>
    /// <param name="valueType">The required metadata value type.</param>
    /// <returns><see langword="true" /> when a value of that type is present.</returns>
    /// <remarks>
    ///     The required type is only known at runtime, so presence is decided by scanning the resolved values. There
    ///     are a handful per message and this runs at composition or probe time, so the cost is irrelevant next to
    ///     reflecting the generic call into place.
    /// </remarks>
    private static bool DeclaresValue(IMessageMetadata metadata, Type valueType)
    {
        foreach (var value in metadata.Values)
        {
            if (valueType.IsInstanceOfType(value))
            {
                return true;
            }
        }

        return false;
    }
}
