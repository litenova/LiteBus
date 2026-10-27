using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Authorization;

/// <summary>
///     A command whose authorization position is declared with an attribute.
/// </summary>
[Authorized("vaults.open")]
internal sealed class OpenVaultCommand : ICommand;

/// <summary>
///     Handles <see cref="OpenVaultCommand" />.
/// </summary>
internal sealed class OpenVaultCommandHandler : ICommandHandler<OpenVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(OpenVaultCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command that records why it needs no authorization rather than omitting a position.
/// </summary>
[AuthorizationExempt("the vault catalogue is public, so there is no actor to authorize")]
internal sealed class BrowseVaultCatalogueCommand : ICommand;

/// <summary>
///     Handles <see cref="BrowseVaultCatalogueCommand" />.
/// </summary>
internal sealed class BrowseVaultCatalogueCommandHandler : ICommandHandler<BrowseVaultCatalogueCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(BrowseVaultCatalogueCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose authorization position is declared in a keyed definition class.
/// </summary>
internal sealed class RotateVaultKeyCommand : ICommand;

/// <summary>
///     Handles <see cref="RotateVaultKeyCommand" />.
/// </summary>
internal sealed class RotateVaultKeyCommandHandler : ICommandHandler<RotateVaultKeyCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(RotateVaultKeyCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Declares the authorization position of <see cref="RotateVaultKeyCommand" />.
/// </summary>
internal sealed class RotateVaultKeyCommandDefinition : IAuthorizationDefinition<RotateVaultKeyCommand>
{
    /// <inheritdoc />
    public AuthorizationDeclaration Authorization => AuthorizationDeclaration.Authorized("vaults.rotate");
}

/// <summary>
///     A command that declares its authorization position from a <c>Describe</c> method.
/// </summary>
internal sealed class SealVaultCommand : ICommand;

/// <summary>
///     Handles <see cref="SealVaultCommand" />.
/// </summary>
internal sealed class SealVaultCommandHandler : ICommandHandler<SealVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(SealVaultCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Declares the authorization position of <see cref="SealVaultCommand" /> with the describe helper.
/// </summary>
internal sealed class SealVaultCommandDefinition : IMessageDefinition<SealVaultCommand>
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarations declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        declarations.Authorized("vaults.seal");
    }
}

/// <summary>
///     A command that describes itself as deliberately exempt from authorization.
/// </summary>
internal sealed class PingVaultCommand : ICommand;

/// <summary>
///     Handles <see cref="PingVaultCommand" />.
/// </summary>
internal sealed class PingVaultCommandHandler : ICommandHandler<PingVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(PingVaultCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records the authorization exemption <see cref="PingVaultCommand" /> relies on.
/// </summary>
internal sealed class PingVaultCommandDefinition : IMessageDefinition<PingVaultCommand>
{
    /// <inheritdoc />
    public void Describe(IMessageDeclarations declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        declarations.AuthorizationExempt("a liveness probe has no actor to authorize");
    }
}

/// <summary>
///     A command declaring no authorization position, used to assert the requirement fails composition.
/// </summary>
internal sealed class DraftVaultCommand : ICommand;

/// <summary>
///     Handles <see cref="DraftVaultCommand" />.
/// </summary>
internal sealed class DraftVaultCommandHandler : ICommandHandler<DraftVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(DraftVaultCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
