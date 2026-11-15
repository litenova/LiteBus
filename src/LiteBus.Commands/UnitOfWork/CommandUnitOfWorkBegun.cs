using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.UnitOfWork;

/// <summary>
///     Marks a command mediation whose unit-of-work transaction was opened.
/// </summary>
/// <remarks>
///     <para>
///         Recorded in the execution-context data by the boundary pre-handler after
///         <see cref="IUnitOfWorkBoundary.BeginAsync" /> returns, and read back by the command unit-of-work completion
///         handler: on any outcome other than <see cref="MediationOutcome.Succeeded" /> it rolls the transaction back
///         only when this marker is present. A guard denial, a validation failure, and a shortcut answer leave no
///         marker, because those stages run before pre-handlers, so they roll back nothing.
///     </para>
///     <para>
///         One marker per mediation is enough: the boundary pre-handler runs once per mediation.
///     </para>
/// </remarks>
internal sealed record CommandUnitOfWorkBegun();
