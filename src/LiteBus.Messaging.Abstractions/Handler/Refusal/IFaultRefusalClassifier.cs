using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Recognizes a fault a stage raised and names the refusal the mediation ends as instead.
/// </summary>
/// <remarks>
///     <para>
///         A fault raised after the decision stages, such as a duplicate key reported when a unit of work commits,
///         never passed a guard, so without a classifier it reaches the caller as an exception and every application
///         maps it by hand at its boundary. A classifier moves that mapping into the pipeline: a recognized fault ends
///         the mediation as the returned refusal, so <c>TrySendAsync</c> reports it as a value, <c>SendAsync</c> raises
///         the refusal exception, refusal mappers see it, and the audit outcome records it.
///     </para>
///     <para>
///         Classifiers are consulted in registration order and the first match wins. They are registered as singletons,
///         so an implementation must be stateless and safe to call from concurrent mediations. A classifier is a pure
///         predicate over the exception: it must not perform I/O, mutate state, or throw. A fault a classifier throws
///         escapes the mediation in place of the fault it was offered.
///     </para>
///     <para>
///         The shipped strategies never offer a cancellation or an exception that already carries a refusal. A classifier
///         therefore sees only genuine faults and needs no branch for either.
///     </para>
/// </remarks>
public interface IFaultRefusalClassifier
{
    /// <summary>
    ///     Determines whether a fault the pipeline raised is a refusal the caller should see.
    /// </summary>
    /// <param name="exception">The fault a stage raised.</param>
    /// <param name="refusal">
    ///     The refusal the mediation ends as when this method returns <see langword="true" />; undefined otherwise.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> when the fault is recognized and <paramref name="refusal" /> carries the refusal;
    ///     otherwise, <see langword="false" />.
    /// </returns>
    bool TryClassify(Exception exception, out Refusal refusal);
}
