using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.NoHandlerStream;

/// <summary>
///     A stream query with no registered handler used to verify failure semantics.
/// </summary>
public sealed class EmptyStreamQuery : IAuditableQuery, IStreamQuery<EmptyStreamQueryResult>
{
    /// <inheritdoc />
    public Guid CorrelationId { get; } = Guid.NewGuid();

    /// <inheritdoc />
    public List<Type> ExecutedTypes { get; } = [];
}

/// <summary>
///     The result type for <see cref="EmptyStreamQuery" />.
/// </summary>
public sealed class EmptyStreamQueryResult
{
    /// <summary>
    ///     Gets or sets the correlation identifier.
    /// </summary>
    public Guid CorrelationId { get; init; }
}