using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.Checkpoints;

/// <summary>
///     A checkpoint policy that names the label a due cut stores on its checkpoint.
/// </summary>
/// <remarks>
///     <para>
///         The label distinguishes checkpoints cut on different rhythms, so a signer and a publisher can switch on it:
///         a daily signed checkpoint for the operator and a weekly one for an external auditor carry different labels
///         while sharing the <see cref="AuditCheckpointKind.Regular" /> lifecycle. A policy that carries no label
///         stores <see langword="null" />, and an explicit cut sets the label through its own parameter.
///     </para>
///     <para>
///         <see cref="CheckpointLifecycle{TDocument}" /> reads this label when <see cref="ICheckpointPolicy.IsDueAsync" />
///         reports due and passes it to the cut, so the stored checkpoint and every signature over it carry the same
///         label the policy chose.
///     </para>
/// </remarks>
public interface ILabeledCheckpointPolicy : ICheckpointPolicy
{
    /// <summary>
    ///     Gets the label a due cut stores on its checkpoint, or <see langword="null" /> for none.
    /// </summary>
    /// <value>
    ///     The free-form label, fixed for the life of the policy.
    /// </value>
    string? Label { get; }
}
