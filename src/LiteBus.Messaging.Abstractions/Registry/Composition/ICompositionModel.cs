using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Read-only view of everything the host composed, for checks and export.
/// </summary>
/// <remarks>
///     <para>
///         Handed to the callback passed to <c>MessageModuleBuilder.ValidateComposition</c>, after every module has
///         built, so it holds every message, declaration, policy, contract, port and contributed section the host
///         composed. Applications use it to enforce their own conventions; hosts log its exported JSON; CI diffs the
///         JSON between builds as a compliance record.
///     </para>
///     <para>
///         It replaces the growth of one property per registry with one model: the message catalog, declaration
///         provenance per message, declared and registered policy names, contract registrations, the resolved
///         implementation of every port, and the sections features contribute through
///         <see cref="ICompositionSectionContributor" />. Nothing here changes runtime behavior; it only makes
///         composition readable.
///     </para>
/// </remarks>
public interface ICompositionModel
{
    /// <summary>
    ///     Gets every registered message and what it declares.
    /// </summary>
    IMessageCatalog Messages { get; }

    /// <summary>
    ///     Gets the declaration provenance records for one message.
    /// </summary>
    /// <param name="messageType">The message type to report.</param>
    /// <returns>
    ///     One record per effective declaration value, in ordinal declaration type name order, or an empty list when
    ///     the message is unknown or declares nothing.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="messageType" /> is <see langword="null" />.</exception>
    IReadOnlyList<MessageDeclarationRecord> DeclarationsFor(Type messageType);

    /// <summary>
    ///     Gets the policy names messages declare.
    /// </summary>
    /// <value>The declared names in ordinal order.</value>
    IReadOnlyCollection<string> DeclaredPolicyNames { get; }

    /// <summary>
    ///     Gets the policy names the composition registered.
    /// </summary>
    /// <value>The registered names in ordinal order.</value>
    IReadOnlyCollection<string> RegisteredPolicyNames { get; }

    /// <summary>
    ///     Gets the contract registrations the composition holds.
    /// </summary>
    /// <value>The registrations in ordinal contract name, then version, order.</value>
    IReadOnlyList<ContractRegistration> Contracts { get; }

    /// <summary>
    ///     Gets the resolved implementation of every port, marked default or replaced.
    /// </summary>
    /// <value>The port entries in ordinal port type name order.</value>
    IReadOnlyList<CompositionPortEntry> Ports { get; }

    /// <summary>
    ///     Gets the sections features contributed to the composition.
    /// </summary>
    /// <value>The contributors in ordinal section name order.</value>
    /// <remarks>
    ///     Refusal mappings, recurring registrations and other feature-owned composition each arrive as one named
    ///     section, so the core reads them without referencing the feature package.
    /// </remarks>
    IReadOnlyList<ICompositionSectionContributor> Sections { get; }

    /// <summary>
    ///     Exports the composition as deterministic JSON for CI diffs and compliance records.
    /// </summary>
    /// <returns>The snapshot JSON, byte-identical across runs for the same composition.</returns>
    /// <remarks>
    ///     <para>
    ///         The document carries a <c>schemaVersion</c> field, the messages with their per-message declaration
    ///         source tables, the declared and registered policy names, the contracts, the resolved ports, and every
    ///         contributed section. Keys sort ordinally at every level, so the output is byte-identical across runs
    ///         and process restarts for the same composition.
    ///     </para>
    /// </remarks>
    string ToJson();
}
