using System.Diagnostics.CodeAnalysis;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     A type-keyed store for values one pipeline stage resolves and a later stage needs.
/// </summary>
/// <remarks>
///     <para>
///         Reach it through <see cref="IExecutionContext.Data" />. It exists because
///         <see cref="IExecutionContext.Items" /> is keyed by string, which is the wrong shape for handing a resolved
///         object forward: the key is invented at both ends, the cast is unchecked, and a rename in one stage is a
///         runtime failure in another.
///     </para>
///     <para>
///         The case it is built for is a guard whose decision depends on loaded state. A guard that has to read an
///         aggregate to decide whether the caller may act would otherwise force the main handler to load the same
///         aggregate again, which is a wasted round trip on every message and the reason authorization tends to stay
///         inside handlers instead of moving into a guard where it belongs. Put the aggregate here and the guard's load
///         is the handler's load.
///     </para>
///     <para>
///         One value per type by default. Wrap a primitive in a named type rather than storing a bare
///         <see cref="string" /> or <see cref="int" />, which two unrelated stages will collide on.
///     </para>
///     <para>
///         Where one mediation legitimately holds several values of one type, pass a key. A command naming two
///         accounts stores each under its own identifier and the handler reads each back by the identifier it already
///         has, which is the identity-map case the unkeyed store cannot express. A keyed entry and an unkeyed one of
///         the same type do not collide: the unkeyed calls address a reserved slot of their own.
///     </para>
///     <para>
///         The store belongs to one mediation and is not shared between mediations. Access is synchronised, so parallel
///         event handlers sharing an execution context can read and write it safely, but a value they race to set is
///         still whichever one landed last.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class CancelOccurrenceGuard : ICommandGuard<CancelOccurrenceCommand>
/// {
///     private readonly IOccurrenceRepository _occurrences;
///     private readonly IAuthorizer _authorizer;
///
///     public CancelOccurrenceGuard(IOccurrenceRepository occurrences, IAuthorizer authorizer)
///     {
///         _occurrences = occurrences;
///         _authorizer = authorizer;
///     }
///
///     public async Task<Verdict> DecideAsync(CancelOccurrenceCommand message, CancellationToken cancellationToken = default)
///     {
///         var occurrence = await _occurrences.LoadAsync(message.OccurrenceId, cancellationToken);
///
///         if (occurrence is null)
///         {
///             return Verdict.Deny("the occurrence does not exist");
///         }
///
///         if (!await _authorizer.MayCancelAsync(occurrence, cancellationToken))
///         {
///             return Verdict.Deny("not permitted to cancel this occurrence");
///         }
///
///         // The handler takes it from here instead of loading it again.
///         AmbientExecutionContext.Current.Data.Set(occurrence);
///         return Verdict.Allow;
///     }
/// }
/// ]]></code>
/// </example>
public interface IHandleContextData
{
    /// <summary>
    ///     Gets the value stored under <typeparamref name="T" />.
    /// </summary>
    /// <typeparam name="T">The type of the value to get, which is also its key.</typeparam>
    /// <returns>The stored value.</returns>
    /// <exception cref="HandleContextDataNotFoundException">No value of that type is present.</exception>
    /// <remarks>
    ///     Use this where an earlier stage is required to have supplied the value, so a missing one is a wiring error
    ///     worth failing on. Use <see cref="TryGet{T}(out T)" /> where the value is optional.
    /// </remarks>
    T Get<T>();

    /// <summary>
    ///     Attempts to get the value stored under <typeparamref name="T" />.
    /// </summary>
    /// <typeparam name="T">The type of the value to get, which is also its key.</typeparam>
    /// <param name="value">When this method returns <see langword="true" />, the stored value.</param>
    /// <returns><see langword="true" /> when a value of that type is present; otherwise, <see langword="false" />.</returns>
    bool TryGet<T>([MaybeNullWhen(false)] out T value);

    /// <summary>
    ///     Stores a value under its own type, replacing any value already stored under that type.
    /// </summary>
    /// <typeparam name="T">The type the value is stored under. Specify it explicitly to store a value under a base type or interface.</typeparam>
    /// <param name="value">The value to store.</param>
    void Set<T>(T value) where T : notnull;

    /// <summary>
    ///     Determines whether a value of type <typeparamref name="T" /> is present.
    /// </summary>
    /// <typeparam name="T">The type to check for.</typeparam>
    /// <returns>
    ///     <see langword="true" /> if a value of type <typeparamref name="T" /> is present; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    bool Contains<T>();

    /// <summary>
    ///     Removes the value stored under <typeparamref name="T" />, if there is one.
    /// </summary>
    /// <typeparam name="T">The type of the value to remove.</typeparam>
    void Remove<T>();

    /// <summary>
    ///     Gets the value stored under <typeparamref name="T" /> and <paramref name="key" />.
    /// </summary>
    /// <typeparam name="T">The type of the value to get.</typeparam>
    /// <param name="key">The key the value was stored under.</param>
    /// <returns>The stored value.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="key" /> is <see langword="null" />.</exception>
    /// <exception cref="HandleContextDataNotFoundException">No value is present for that type and key.</exception>
    T Get<T>(object key);

    /// <summary>
    ///     Attempts to get the value stored under <typeparamref name="T" /> and <paramref name="key" />.
    /// </summary>
    /// <typeparam name="T">The type of the value to get.</typeparam>
    /// <param name="key">The key the value was stored under.</param>
    /// <param name="value">When this method returns <see langword="true" />, the stored value.</param>
    /// <returns><see langword="true" /> when a value is present for that type and key.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="key" /> is <see langword="null" />.</exception>
    bool TryGet<T>(object key, [MaybeNullWhen(false)] out T value);

    /// <summary>
    ///     Stores a value under <typeparamref name="T" /> and <paramref name="key" />, replacing any value already
    ///     stored under that pair.
    /// </summary>
    /// <typeparam name="T">The type the value is stored under.</typeparam>
    /// <param name="key">
    ///     The key that separates this value from others of the same type. An identifier value object is the usual
    ///     choice, because the reader already holds it; keys are compared with <see cref="object.Equals(object)" />.
    /// </param>
    /// <param name="value">The value to store.</param>
    /// <exception cref="System.ArgumentNullException">
    ///     <paramref name="key" /> or <paramref name="value" /> is <see langword="null" />.
    /// </exception>
    void Set<T>(object key, T value) where T : notnull;

    /// <summary>
    ///     Determines whether a value is present for <typeparamref name="T" /> and <paramref name="key" />.
    /// </summary>
    /// <typeparam name="T">The type to check for.</typeparam>
    /// <param name="key">The key to check for.</param>
    /// <returns><see langword="true" /> when a value is present for that type and key.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="key" /> is <see langword="null" />.</exception>
    bool Contains<T>(object key);

    /// <summary>
    ///     Removes the value stored under <typeparamref name="T" /> and <paramref name="key" />, if there is one.
    /// </summary>
    /// <typeparam name="T">The type of the value to remove.</typeparam>
    /// <param name="key">The key the value was stored under.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="key" /> is <see langword="null" />.</exception>
    void Remove<T>(object key);
}
