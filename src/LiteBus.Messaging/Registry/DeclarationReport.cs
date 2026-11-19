using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     The report-mode declaration requirements the <c>litebus.declarations</c> probe evaluates.
/// </summary>
/// <remarks>
///     Registered once as a singleton when a requirement uses report enforcement. The probe reads the message catalog
///     at check time, so the list reflects every message the host composed rather than only the ones registered when
///     the requirement was added.
/// </remarks>
internal sealed class DeclarationReport
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DeclarationReport" /> class.
    /// </summary>
    /// <param name="requirements">The report-mode requirements, in the order they were added.</param>
    public DeclarationReport(IReadOnlyList<DeclarationRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        Requirements = requirements;
    }

    /// <summary>
    ///     Gets the report-mode requirements the probe evaluates.
    /// </summary>
    public IReadOnlyList<DeclarationRequirement> Requirements { get; }
}
