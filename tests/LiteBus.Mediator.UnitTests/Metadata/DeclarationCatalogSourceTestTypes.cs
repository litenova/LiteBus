using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Metadata;

/// <summary>
///     Declares the permissions of the two commands that carry no definition of their own.
/// </summary>
internal sealed class FamilyCatalogSource : IMultiMessageDefinition
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        catalog
            .Describe<SweepStaleLocksCommand>(declarations => declarations.Declare(new RequiredAuthorization("locks.sweep")))
            .Describe<UnattributedTransferCommand>(declarations => declarations.Declare(new RequiredAuthorization("money.transfer-source")));
    }
}

/// <summary>
///     Declares a conflicting permission for a command that already declares one through a definition.
/// </summary>
internal sealed class ClashingCatalogSource : IMultiMessageDefinition
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        catalog.Describe<TransferFundsCommand>(declarations => declarations.Declare(new RequiredAuthorization("money.other")));
    }
}

/// <summary>
///     Repeats the permission a definition already declares, so the repeat is a no-op.
/// </summary>
internal sealed class MatchingCatalogSource : IMultiMessageDefinition
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        catalog.Describe<TransferFundsCommand>(declarations => declarations.Declare(new RequiredAuthorization("money.transfer")));
    }
}

/// <summary>
///     Deliberately overwrites the permission a definition declares.
/// </summary>
internal sealed class ReplacingCatalogSource : IMultiMessageDefinition
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        catalog.Replace<TransferFundsCommand>(declarations => declarations.Declare(new RequiredAuthorization("money.transfer-replaced")));
    }
}

/// <summary>
///     Declares one value type twice for one message, which is a typo the catalog reports.
/// </summary>
internal sealed class DoubleDeclaringCatalogSource : IMultiMessageDefinition
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        catalog
            .Describe<SweepStaleLocksCommand>(declarations => declarations.Declare(new RequiredAuthorization("first")))
            .Describe<SweepStaleLocksCommand>(declarations => declarations.Declare(new RequiredAuthorization("second")));
    }
}

/// <summary>
///     Declares nothing, which is indistinguishable from a source nobody finished.
/// </summary>
internal sealed class EmptyCatalogSource : IMultiMessageDefinition
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarationCatalog catalog)
    {
    }
}
