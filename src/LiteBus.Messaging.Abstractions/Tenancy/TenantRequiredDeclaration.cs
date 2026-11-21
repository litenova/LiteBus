namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Declares that a message must run inside a tenant; the tenancy guard refuses it when the execution identity has no
///     tenant.
/// </summary>
public sealed record TenantRequiredDeclaration : TenancyDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TenantRequiredDeclaration" /> class.
    /// </summary>
    private TenantRequiredDeclaration()
    {
    }

    /// <summary>
    ///     Gets the single instance of the declaration.
    /// </summary>
    public static TenantRequiredDeclaration Instance { get; } = new();

    /// <inheritdoc />
    public override bool RequiresTenant => true;
}
