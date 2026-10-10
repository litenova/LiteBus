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
    ///     Gets every audited message and the action it declares.
    /// </summary>
    /// <returns>One entry per message declaring an <see cref="AuditedDeclaration" />.</returns>
    /// <remarks>
    ///     The audit catalogue is the compliance artifact teams most often maintain by hand and keep wrong, and it is a
    ///     pure function of the declarations. This is the traversal to build it from.
    /// </remarks>
    IEnumerable<MessageCatalogEntry> Audited();
}
