using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Adapts a query validator that signals failure by throwing to the <see cref="Validity" /> contract.
/// </summary>
/// <typeparam name="TQuery">The query type this validator runs for.</typeparam>
/// <typeparam name="TException">The exception type the validation body throws to report failure.</typeparam>
/// <remarks>
///     Migration scaffolding, meant to be deleted once every validator returns <see cref="Validity" /> directly. See
///     <see cref="ThrowingValidator{TMessage,TException}" /> for what it does and what it cannot recover. This
///     specialization exists because the query module registers query constructs only, and a validator implementing the
///     messaging contract alone is refused.
/// </remarks>
public abstract class ThrowingQueryValidator<TQuery, TException>
    : ThrowingValidator<TQuery, TException>, IQueryValidator<TQuery>
    where TQuery : IQuery
    where TException : Exception;
