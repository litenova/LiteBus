using System.ComponentModel;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     Which axes the application asked <c>AddAuditing</c> to audit, shared from the messaging module to the axis
///     modules that build after it.
/// </summary>
/// <remarks>
///     <para>
///         The auditing feature has one plumbed half and one per-axis half: a trail, an actor resolver and an outcome
///         mapper belong to messaging, and the completion handler that writes a record belongs to each axis. Before
///         this, a consumer had to make the same decision on two or three builders and could configure a trail with no
///         axis enabled, or an axis with no trail, and only find out from a diagnostic probe.
///     </para>
///     <para>
///         Passing the decision through the shared module context rather than through a new module keeps each axis
///         package owning its own completion handler. An auditing module would have to reference all three axes, which
///         the dependency role rules do not allow.
///     </para>
///     <para>
///         It is public only because the axis packages are separate assemblies that have to read it at compose time.
///         Nothing an application writes should name it; configure the feature through <c>AddAuditing</c>.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AuditingComposition
{
    /// <summary>
    ///     Gets or sets a value indicating whether command mediations produce audit records.
    /// </summary>
    public bool Commands { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether query mediations produce audit records.
    /// </summary>
    public bool Queries { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether event mediations produce audit records.
    /// </summary>
    public bool Events { get; set; }

    /// <summary>
    ///     Gets a value indicating whether any axis was selected.
    /// </summary>
    /// <value>
    ///     <see langword="false" /> when a trail was configured and no axis was, which means the feature is wired and
    ///     inert. The <c>litebus.audit.trail</c> probe reports that rather than leaving it silent.
    /// </value>
    public bool AnyAxis => Commands || Queries || Events;
}
