using System;

namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Declares that a message runs across tenants under a stated reason, such as a retention or integrity job.
/// </summary>
public sealed record CrossTenantDeclaration : TenancyDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CrossTenantDeclaration" /> class.
    /// </summary>
    /// <param name="reason">Why the message may act across tenants.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="reason" /> is null or whitespace.</exception>
    public CrossTenantDeclaration(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <inheritdoc />
    public override bool RequiresTenant => false;

    /// <summary>
    ///     Gets why the message may act across tenants.
    /// </summary>
    public string Reason { get; init; }
}
