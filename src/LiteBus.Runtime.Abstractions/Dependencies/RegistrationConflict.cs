using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Applies the registration conflict rule shared by every registration API in LiteBus.
/// </summary>
/// <remarks>
///     An identical repeat is a no-op, a conflicting repeat fails composition naming both sources,
///     and a deliberate override calls <c>Replace</c> on the owning registry or builder instead of
///     calling this helper. Identical means value equality through
///     <see cref="EqualityComparer{T}.Default" />, which falls back to reference equality for
///     values without it.
/// </remarks>
public static class RegistrationConflict
{
    /// <summary>
    ///     Determines whether two registered values are identical under the conflict rule.
    /// </summary>
    /// <typeparam name="T">The registered value type.</typeparam>
    /// <param name="existing">The value already registered.</param>
    /// <param name="incoming">The value being registered.</param>
    /// <returns>
    ///     <see langword="true" /> when the values are identical and the repeat is a no-op;
    ///     otherwise, <see langword="false" />.
    /// </returns>
    public static bool IsIdentical<T>(T? existing, T? incoming)
    {
        return EqualityComparer<T>.Default.Equals(existing, incoming);
    }

    /// <summary>
    ///     Returns without effect when two registered values are identical, and fails composition
    ///     naming both sources when they conflict.
    /// </summary>
    /// <typeparam name="T">The registered value type.</typeparam>
    /// <param name="existing">The value already registered.</param>
    /// <param name="incoming">The value being registered.</param>
    /// <param name="registrationName">The registration being made, such as a service type or policy name.</param>
    /// <param name="existingSource">The source that supplied <paramref name="existing" />.</param>
    /// <param name="incomingSource">The source that supplied <paramref name="incoming" />.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <paramref name="existing" /> and <paramref name="incoming" /> differ. The message
    ///     names <paramref name="existingSource" /> and <paramref name="incomingSource" /> and points at
    ///     <c>Replace</c> for a deliberate override.
    /// </exception>
    public static void ThrowIfConflicting<T>(T? existing, T? incoming, string registrationName, string existingSource, string incomingSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(existingSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(incomingSource);

        if (IsIdentical(existing, incoming))
        {
            return;
        }

        throw new LiteBusConfigurationException(
            $"{registrationName} is already registered by {existingSource}. " +
            $"The registration from {incomingSource} conflicts with it. " +
            "Call Replace to override the existing registration.");
    }
}
