using System;

namespace LiteBus.Commands.UnitOfWork;

/// <summary>
///     Records, at composition time, the unit-of-work type registered on the command module through
///     <c>UseUnitOfWork</c>.
/// </summary>
/// <remarks>
///     Registered as a singleton only when a unit of work is registered, so its presence answers "does a command commit
///     run after this mediation" without scanning the handler registry on every mediation. A command batch also resolves
///     the unit of work through <see cref="UnitOfWorkType" />, because the separate <c>IUnitOfWork</c> mapping can be a
///     different scoped instance from the one the completion handler commits.
/// </remarks>
/// <param name="UnitOfWorkType">The concrete unit-of-work type registered on the command module.</param>
internal sealed record CommandUnitOfWorkRegistration(Type UnitOfWorkType);
