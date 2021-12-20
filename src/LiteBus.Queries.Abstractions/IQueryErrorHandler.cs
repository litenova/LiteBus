using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

public interface IQueryErrorHandlerBase : IQueryConstruct
{
}

/// <summary>
///     Represents an action that is executed on each query error-handle phase
/// </summary>
public interface IQueryErrorHandler : IQueryErrorHandlerBase, IMessageErrorHandler<IQueryBase>
{
}

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TQuery" /> error-handle phase
/// </summary>
public interface IQueryErrorHandler<in TQuery> : IQueryErrorHandlerBase, IMessageErrorHandler<TQuery>
    where TQuery : IQueryBase
{
}