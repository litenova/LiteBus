using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Who performed an audited action.
/// </summary>
/// <remarks>
///     <para>
///         This is the first column of every audit trail that has ever existed, and the first question a review asks.
///         An <see cref="AuditRecord" /> without it says what happened and cannot say who is answerable for it.
///     </para>
///     <para>
///         The shape follows the initiator of the DMTF CADF event model and the actor of NIST SP 800-53 AU-3: a stable
///         identifier, a kind that says what sort of thing acted, and a display name for a reader who should not have
///         to resolve the identifier. It is deliberately data rather than a closed hierarchy, because the set
///         of things that can act is the application's to define. A service account, a scanner at a door, and a
///         scheduled worker are all actors, and LiteBus cannot enumerate them.
///     </para>
///     <para>
///         Supply it from an <see cref="IAuditActorResolver" />, which sees the message and runs on every path
///         including a denial, or from <see cref="IAuditScope.WithActor" /> where only the handler knows. A resolver
///         may legitimately return <see langword="null" />: a command replayed from the inbox carries whatever the
///         envelope recorded, and a command raised by a background loop has no actor at all. Prefer stating that
///         outright with <see cref="System(string)" /> over inventing an identifier, and leave the record's actor null only
///         where nothing established one, which is a defect worth being able to see.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// AuditActor.User(account.Id.ToString());
/// AuditActor.System("nightly-settlement");
/// AuditActor.For("scanner-device", device.Id.ToString()) with { OnBehalfOf = authorizedBy.ToString() };
///
/// // Recording a display name puts personal data in the trail, so it is a deliberate act rather than a
/// // second argument. See the note on DisplayName.
/// AuditActor.User(account.Id.ToString()) with { DisplayName = account.Email };
/// ]]></code>
/// </example>
public sealed record AuditActor
{
    /// <summary>
    ///     The kind reported for a person acting through an authenticated request.
    /// </summary>
    public const string UserKind = "user";

    /// <summary>
    ///     The kind reported for a named process acting with no person behind the request.
    /// </summary>
    public const string SystemKind = "system";

    /// <summary>
    ///     Gets the stable identifier of the actor.
    /// </summary>
    /// <value>
    ///     Whatever the application uses to identify the actor durably: an account identifier, a device identifier, or
    ///     the name of a worker. It has to stay resolvable for as long as the trail is retained, so prefer a surrogate
    ///     key over an address or a display name that can be reassigned.
    /// </value>
    public required string Id { get; init; }

    /// <summary>
    ///     Gets the kind of thing that acted.
    /// </summary>
    /// <value>
    ///     A stable code such as <see cref="UserKind" />, <see cref="SystemKind" />, or one the application defines.
    ///     It exists so a query can separate the actions people took from the actions a process took, which is a
    ///     distinction every review draws and which an identifier alone cannot express.
    /// </value>
    /// <remarks>
    ///     Required, because an actor of no kind is a state no audit query can use and no factory here produces. It
    ///     stays a string rather than an enumeration for the reason given on the type: the set of things that can act
    ///     belongs to the application, and a closed set belongs at the point the application constructs the actor.
    /// </remarks>
    public required string Kind { get; init; }

    /// <summary>
    ///     Gets the name to show a reader in place of the identifier.
    /// </summary>
    /// <value>
    ///     The name as it stood when the action happened, or <see langword="null" /> to leave resolution to the reader.
    ///     Recording it makes the entry readable after the account is deleted, and makes the trail hold personal data;
    ///     which of those matters more is the application's decision, so LiteBus does not populate it.
    /// </value>
    /// <remarks>
    ///     Set it with <c>with { DisplayName = name }</c>. None of the factories here take it, deliberately: putting
    ///     personal data behind an optional second argument makes it the easiest thing to reach for at a call site,
    ///     and a decision with a data-protection consequence should be visible in the line that makes it.
    /// </remarks>
    public string? DisplayName { get; init; }

    /// <summary>
    ///     Gets the actor this one acted for, where the action was delegated.
    /// </summary>
    /// <value>
    ///     The identifier of the delegating actor, or <see langword="null" /> when the actor acted for itself. This is
    ///     the field that separates support staff acting as a customer from the customer acting, and a device acting on
    ///     a key from the person who authorized that key. A nullable actor cannot express either.
    /// </value>
    public string? OnBehalfOf { get; init; }

    /// <summary>
    ///     Creates an actor for a person acting through an authenticated request.
    /// </summary>
    /// <param name="id">The stable identifier of the account.</param>
    /// <returns>The actor, ready to be refined with <c>with</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="id" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     Add a display name with <c>with { DisplayName = name }</c> where the application has decided the trail
    ///     should hold one. See <see cref="DisplayName" /> for why it is not a parameter here.
    /// </remarks>
    public static AuditActor User(string id)
    {
        return For(UserKind, id);
    }

    /// <summary>
    ///     Creates an actor for a named process acting with no person behind the request.
    /// </summary>
    /// <param name="processName">The worker or reaction, named so the record says which.</param>
    /// <returns>The actor.</returns>
    /// <exception cref="ArgumentException"><paramref name="processName" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     A scheduled job and an unattributed action are different answers, and an audit query has to be able to tell
    ///     them apart. Naming the process says a worker did this; leaving the actor null says nobody recorded who did.
    /// </remarks>
    public static AuditActor System(string processName)
    {
        return For(SystemKind, processName);
    }

    /// <summary>
    ///     Creates an actor of an application-defined kind.
    /// </summary>
    /// <param name="kind">The stable code for the kind of thing that acted.</param>
    /// <param name="id">The stable identifier of the actor.</param>
    /// <returns>The actor, ready to be refined with <c>with</c>.</returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="kind" /> or <paramref name="id" /> is null, empty, or whitespace.
    /// </exception>
    /// <remarks>
    ///     Add a display name with <c>with { DisplayName = name }</c>. See <see cref="DisplayName" /> for why it is
    ///     not a parameter here.
    /// </remarks>
    public static AuditActor For(string kind, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return new AuditActor
        {
            Id = id,
            Kind = kind
        };
    }
}
