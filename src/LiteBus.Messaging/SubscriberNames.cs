using System;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging;

/// <summary>
///     Resolves the stable subscriber name that per-handler outbox delivery records for an event handler.
/// </summary>
/// <remarks>
///     <para>
///         A main handler carrying <see cref="SubscriberNameAttribute" /> is recorded under that name; any other
///         handler is recorded under its full type name, exactly as before subscriber names existed. The outbox
///         envelope factory writes the resolved name and derives the per-handler idempotency key and message
///         identifier from it, while dispatch matches a recorded value against both the subscriber name and the full
///         type name, so an envelope written before the attribute was added still reaches the handler.
///     </para>
/// </remarks>
public static class SubscriberNames
{
    /// <summary>
    ///     The maximum length of a subscriber name in characters.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    ///     Resolves the subscriber name recorded for a handler type.
    /// </summary>
    /// <param name="handlerType">The handler type.</param>
    /// <returns>
    ///     The <see cref="SubscriberNameAttribute.Name" /> declared on the handler, or the handler's full type name
    ///     when it carries no attribute.
    /// </returns>
    /// <exception cref="LiteBusConfigurationException">The declared name is empty.</exception>
    public static string Resolve(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);

        return GetDeclaredName(handlerType) ?? FullNameOrName(handlerType);
    }

    /// <summary>
    ///     Determines whether a recorded handler value addresses a handler type.
    /// </summary>
    /// <param name="handlerType">The handler type.</param>
    /// <param name="recordedHandler">The handler value recorded on the envelope.</param>
    /// <returns>
    ///     <see langword="true" /> when the recorded value equals the handler's subscriber name or its full type
    ///     name; otherwise <see langword="false" />.
    /// </returns>
    /// <exception cref="LiteBusConfigurationException">The declared name is empty.</exception>
    /// <remarks>
    ///     Both spellings are accepted because an envelope written before <see cref="SubscriberNameAttribute" />
    ///     was added carries the full type name, while an envelope written after carries the subscriber name. Add
    ///     the attribute first, deploy, let the old envelopes drain, and only then rename the class.
    /// </remarks>
    public static bool Matches(Type handlerType, string? recordedHandler)
    {
        ArgumentNullException.ThrowIfNull(handlerType);

        if (recordedHandler is null)
        {
            return false;
        }

        return string.Equals(recordedHandler, Resolve(handlerType), StringComparison.Ordinal) ||
            string.Equals(recordedHandler, FullNameOrName(handlerType), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Validates the subscriber name declared on a handler type.
    /// </summary>
    /// <param name="handlerType">The handler type.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     The declared name is empty, longer than <see cref="MaxLength" /> characters, or uses characters outside
    ///     letters, digits, <c>.</c>, <c>-</c> and <c>_</c>.
    /// </exception>
    /// <remarks>
    ///     A handler without the attribute is valid: it is recorded under its full type name, which keeps whatever
    ///     characters the type name holds.
    /// </remarks>
    public static void ThrowIfInvalid(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);

        var name = GetDeclaredName(handlerType);

        if (name is null || IsValidName(name))
        {
            return;
        }

        throw new LiteBusConfigurationException(
            $"Handler '{FullNameOrName(handlerType)}' declares an invalid subscriber name '{name}': " +
            $"the name must be non-empty, at most {MaxLength} characters, and use only letters, digits, '.', '-' and '_'.");
    }

    /// <summary>
    ///     Reads the subscriber name declared on a handler type.
    /// </summary>
    /// <param name="handlerType">The handler type.</param>
    /// <returns>The declared name, or <see langword="null" /> when the handler carries no attribute.</returns>
    /// <exception cref="LiteBusConfigurationException">The declared name is empty.</exception>
    /// <remarks>
    ///     The attribute constructor rejects an empty name with <see cref="ArgumentException" />, which surfaces
    ///     here when reflection instantiates it. It is translated to <see cref="LiteBusConfigurationException" />
    ///     so every invalid subscriber name fails composition with the same category.
    /// </remarks>
    private static string? GetDeclaredName(Type handlerType)
    {
        try
        {
            return handlerType.GetCustomAttribute<SubscriberNameAttribute>(inherit: false)?.Name;
        }
        catch (ArgumentException exception)
        {
            throw new LiteBusConfigurationException(
                $"Handler '{FullNameOrName(handlerType)}' declares an invalid subscriber name: " +
                "the name must be non-empty.",
                exception);
        }
    }

    /// <summary>
    ///     Determines whether a subscriber name uses only the permitted length and characters.
    /// </summary>
    /// <param name="name">The subscriber name to check.</param>
    /// <returns>
    ///     <see langword="true" /> when the name is non-empty, at most <see cref="MaxLength" /> characters, and uses
    ///     only letters, digits, <c>.</c>, <c>-</c> and <c>_</c>; otherwise <see langword="false" />.
    /// </returns>
    private static bool IsValidName(string name)
    {
        if (name.Length == 0 || name.Length > MaxLength)
        {
            return false;
        }

        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];

            if (character is '.' or '-' or '_' || char.IsLetterOrDigit(character))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    ///     Returns the full type name of a handler, falling back to the simple name when the runtime reports none.
    /// </summary>
    /// <param name="handlerType">The handler type.</param>
    /// <returns>The full type name, or the simple name when the full name is unavailable.</returns>
    private static string FullNameOrName(Type handlerType)
    {
        return handlerType.FullName ?? handlerType.Name;
    }
}
