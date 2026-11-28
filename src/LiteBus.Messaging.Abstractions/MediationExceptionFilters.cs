using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Classifies exceptions raised during mediation, so the pipeline and the durable processors treat a decision
///     differently from a fault.
/// </summary>
public static class MediationExceptionFilters
{
    /// <summary>
    ///     The type-name suffix Entity Framework Core and Marten concurrency faults share.
    /// </summary>
    private const string ConcurrencyExceptionSuffix = "ConcurrencyException";

    /// <summary>
    ///     The Marten existence races reported without the concurrency suffix.
    /// </summary>
    private static readonly string[] MartenConflictTypeNames =
    [
        "ConcurrentUpdateException",
        "DocumentAlreadyExistsException",
    ];

    /// <summary>
    ///     The name of the error-number property on SQL Server and MySQL driver exceptions.
    /// </summary>
    private const string ErrorNumberProperty = "Number";

    /// <summary>
    ///     The name of the result-code property on SQLite driver exceptions.
    /// </summary>
    private const string SqliteErrorCodeProperty = "SqliteErrorCode";

    /// <summary>
    ///     Caches the integer property, or its absence, for each exception type and property name, so the reflection
    ///     lookup runs once per driver exception type for the life of the process.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> IntegerProperties = new();
    /// <summary>
    ///     Determines whether an exception should be handled by registered error handlers.
    /// </summary>
    /// <param name="exception">The exception raised during mediation.</param>
    /// <returns>
    ///     <see langword="true" /> when error handlers may run; <see langword="false" /> when the exception must propagate.
    /// </returns>
    /// <remarks>
    ///     Handler resolution failures are configuration errors rather than message failures, cancellation is the caller's
    ///     decision, a refusal is the pipeline's own decision rather than a fault, and an idempotency misconfiguration
    ///     is a declaration mistake rather than a message failure, so all of them propagate unchanged.
    ///     Routing a refusal to error handlers would let a handler written for failures recover from one.
    /// </remarks>
    public static bool IsRecoverableMediationException(Exception exception)
    {
        return exception is not NoHandlerFoundException
            and not MultipleHandlerFoundException
            and not OperationCanceledException
            and not IdempotencyConfigurationException
            && !IsRefusal(exception);
    }

    /// <summary>
    ///     Offers a fault to the registered refusal classifiers, in registration order, until one recognizes it.
    /// </summary>
    /// <param name="classifiers">The classifiers to consult, in the order they were registered.</param>
    /// <param name="exception">The fault a stage raised.</param>
    /// <param name="refusal">
    ///     The refusal the first matching classifier returned when this method returns <see langword="true" />;
    ///     undefined otherwise.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> when a classifier recognized the fault; otherwise, <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     Cancellation is never classified: an <see cref="OperationCanceledException" /> reports the caller's own
    ///     decision, so classifying it would let a refusal swallow a cancellation. An exception that already carries
    ///     a refusal is not offered either, because it is a decision rather than a fault and a second classification
    ///     would rewrite one. A <see cref="MessageFaultException" /> is never classified either: it reports a
    ///     configuration or dependency failure an operator must fix, so no offer site may rewrite it into a refusal
    ///     the caller can act on. Callers offer only recoverable faults, so a configuration error never reaches a
    ///     classifier.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="classifiers" /> or <paramref name="exception" /> is <see langword="null" />.
    /// </exception>
    public static bool TryClassifyRefusal(
        IReadOnlyList<IFaultRefusalClassifier> classifiers,
        Exception exception,
        out Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(classifiers);
        ArgumentNullException.ThrowIfNull(exception);

        refusal = default;

        if (exception is OperationCanceledException || exception is MessageFaultException || IsRefusal(exception))
        {
            return false;
        }

        foreach (var classifier in classifiers)
        {
            if (classifier.TryClassify(exception, out refusal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether an exception carries a pre-stage refusal rather than a fault.
    /// </summary>
    /// <param name="exception">The exception raised during mediation.</param>
    /// <returns><see langword="true" /> for a guard denial or a validation failure.</returns>
    /// <remarks>
    ///     A refusal is a decision the pipeline made about the message itself, so it is reproducible: the same message
    ///     refused once is refused every time. Durable processors use this to retire such a message on its first attempt
    ///     rather than spending the retry schedule on an answer that cannot change.
    /// </remarks>
    public static bool IsRefusal(Exception exception)
    {
        return exception is LiteBusMessageDeniedException or LiteBusMessageInvalidException;
    }

    /// <summary>
    ///     Determines whether an exception reports a lost storage race worth retrying.
    /// </summary>
    /// <param name="exception">The exception that ended the mediation or dispatch.</param>
    /// <returns>
    ///     <see langword="true" /> when the exception graph carries an optimistic-concurrency fault or a database
    ///     deadlock, serialization failure, or lock wait; otherwise, <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Two workers reaching the same record at the same time collide either above the database, as an
    ///         optimistic-concurrency fault, or inside it, as a deadlock victim or a failed serialization. Both are
    ///         the same lost race: the winner already did the work, and the loser usually succeeds by reading again.
    ///     </para>
    ///     <para>
    ///         The decision reads what the persistence library attaches, never message text: an exception type name
    ///         ending in <c>ConcurrencyException</c> (Entity Framework Core <c>DbUpdateConcurrencyException</c>, the
    ///         <c>ConcurrencyException</c> Marten raises, <c>SagaConcurrencyException</c>), the Marten existence races
    ///         <c>ConcurrentUpdateException</c> and <c>DocumentAlreadyExistsException</c>, or a driver error carrying
    ///         SQLSTATE <c>40001</c> (serialization failure, which MySQL also reports for a deadlock) or <c>40P01</c>
    ///         (PostgreSQL deadlock) through <see cref="DbException.SqlState" />, error number <c>1205</c> (SQL Server
    ///         deadlock victim, MySQL lock wait timeout) or <c>1213</c> (MySQL deadlock) on a public <c>Number</c>
    ///         property, or result code <c>5</c> (busy) or <c>6</c> (locked) on a public <c>SqliteErrorCode</c>
    ///         property. The two properties are read by name, which keeps this package free of database driver
    ///         references. The whole exception graph is walked: the <see cref="Exception.InnerException" /> chain and
    ///         every exception an <see cref="AggregateException" /> carries.
    ///     </para>
    ///     <para>
    ///         A unique-violation code is not a lost race: a duplicate key reports deterministic state that reads the
    ///         same on every attempt, so it is excluded even though it also arrives as a driver error.
    ///     </para>
    /// </remarks>
    public static bool IsConcurrencyConflict(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var current in EnumerateExceptions(exception))
        {
            if (IsConcurrencyConflictType(current) || IsDatabaseConflict(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether one exception is an optimistic-concurrency or existence-race fault by type name.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns>
    ///     <see langword="true" /> when the type name ends in <c>ConcurrencyException</c> or names a Marten
    ///     existence race; otherwise, <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     Matching by name keeps this package free of persistence references while recognizing the conflict types
    ///     common .NET persistence libraries throw.
    /// </remarks>
    private static bool IsConcurrencyConflictType(Exception exception)
    {
        var name = exception.GetType().Name;

        if (name.EndsWith(ConcurrencyExceptionSuffix, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var conflictName in MartenConflictTypeNames)
        {
            if (string.Equals(name, conflictName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether one exception is a driver error reporting a lost race.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns><see langword="true" /> for a deadlock, serialization failure, or lock contention code.</returns>
    private static bool IsDatabaseConflict(Exception exception)
    {
        if (exception is not DbException databaseError)
        {
            return false;
        }

        if (databaseError.SqlState is "40001" or "40P01")
        {
            return true;
        }

        if (TryReadInteger(databaseError, ErrorNumberProperty, out var number))
        {
            return number is 1205 or 1213;
        }

        return TryReadInteger(databaseError, SqliteErrorCodeProperty, out var sqliteCode) && sqliteCode is 5 or 6;
    }

    /// <summary>
    ///     Reads a public integer property from a driver exception.
    /// </summary>
    /// <param name="databaseError">The driver error.</param>
    /// <param name="propertyName">The property name.</param>
    /// <param name="value">The property value when the property exists.</param>
    /// <returns><see langword="true" /> when the exception type has the property; otherwise, <see langword="false" />.</returns>
    private static bool TryReadInteger(DbException databaseError, string propertyName, out int value)
    {
        var property = IntegerProperties.GetOrAdd(
            (databaseError.GetType(), propertyName),
            static key => key.Type.GetProperty(key.Name, BindingFlags.Public | BindingFlags.Instance) is { } candidate
                          && candidate.PropertyType == typeof(int)
                ? candidate
                : null);

        if (property?.GetValue(databaseError) is int number)
        {
            value = number;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    ///     Enumerates an exception, its inner exceptions, and every exception an <see cref="AggregateException" />
    ///     carries, depth first.
    /// </summary>
    /// <param name="exception">The exception to start from.</param>
    /// <returns>Every exception reachable from <paramref name="exception" />.</returns>
    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            if (current is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
    }

    /// <summary>
    ///     Determines whether a failed dispatch is worth attempting again.
    /// </summary>
    /// <param name="exception">The exception that ended the dispatch.</param>
    /// <returns>
    ///     <see langword="false" /> for a refusal or a configuration error, which produce the same outcome on every
    ///     attempt; otherwise <see langword="true" />.
    /// </returns>
    /// <remarks>
    ///     Retrying exists for transient conditions such as a dropped connection or a locked row. A message no handler
    ///     is registered for, or one a guard refuses, fails identically on every attempt, so retrying it delays the
    ///     dead-letter entry an operator is waiting to see and occupies the processor while doing it.
    ///     <para>
    ///         The inner exception chain is inspected as well, so a dispatcher that wraps a refusal or a missing handler in
    ///         its own exception type still has it retired on the first attempt.
    ///     </para>
    /// </remarks>
    public static bool IsRetryableDispatchException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NoHandlerFoundException or MultipleHandlerFoundException || IsRefusal(current))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Determines whether an exception reports the caller's own cancellation rather than a dispatch failure.
    /// </summary>
    /// <param name="exception">The exception that ended the dispatch.</param>
    /// <param name="cancellationToken">The token the caller passed to the dispatch.</param>
    /// <returns>
    ///     <see langword="true" /> when <paramref name="cancellationToken" /> is cancelled and the exception, or one of
    ///     its inner exceptions, is an <see cref="OperationCanceledException" />.
    /// </returns>
    /// <remarks>
    ///     Durable processors use this to leave a cancelled message untouched: a host shutdown, a drain, or a dispatch
    ///     timeout must not consume an attempt, record an error, or move the message's next visibility. The inner chain is
    ///     inspected because a dispatcher may wrap the cancellation in its own exception type. A cancellation raised by
    ///     a token other than the caller's is a failure like any other and returns <see langword="false" />.
    /// </remarks>
    public static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }
}
