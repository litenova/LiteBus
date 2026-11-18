using System;

namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Declares that a message belongs to no tenant, such as sign-in or a system-wide health probe.
/// </summary>
public sealed record NoTenantDeclaration : TenancyDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NoTenantDeclaration" /> class.
    /// </summary>
    /// <param name="reason">Why the message has no tenant.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is null or whitespace.</exception>
    public NoTenantDeclaration(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <inheritdoc />
    public override bool RequiresTenant => false;

    /// <summary>
    ///     Gets why the message has no tenant.
    /// </summary>
    public string Reason { get; init; }
}
