namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Base interface for all query types in the system.
/// </summary>
/// <remarks>
///     This interface serves as a common base for all queries in the CQRS pattern.
///     Queries represent read operations that don't modify system state and are used to retrieve data.
///     In practice, developers will typically implement the generic version <see cref="IQuery{TQueryResult}" />
///     which specifies the return type of the query.
/// </remarks>
public interface IQuery : IRegistrableQueryConstruct;