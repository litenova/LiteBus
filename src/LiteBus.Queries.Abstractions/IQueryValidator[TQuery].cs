using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Decides whether a query of type <typeparamref name="TQuery" /> is well-formed.
/// </summary>
/// <typeparam name="TQuery">The specific query type this validator runs for.</typeparam>
/// <remarks>
///     A validator returns <see cref="Validity" /> rather than throwing, so a malformed query reports
///     <see cref="MessageOutcome.Invalid" /> instead of arriving at error handlers as a fault. Every validator for the
///     query runs and their failures are collected, so the caller sees all of them at once.
/// </remarks>
public interface IQueryValidator<in TQuery> : IMessageValidator<TQuery>
    where TQuery : IQuery;
