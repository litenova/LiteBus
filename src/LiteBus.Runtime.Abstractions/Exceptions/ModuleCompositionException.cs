using System;

namespace LiteBus.Runtime.Abstractions.Exceptions;

/// <summary>
///     Thrown when the module graph itself is invalid: a module registered twice, a dependency cycle, a missing required module, or a dependency descriptor the container adapter cannot translate.
/// </summary>
/// <remarks>
///     The failure is in how the host was assembled rather than in any one feature. It is the category to catch
///     when a host composes modules from configuration and wants to report an assembly mistake distinctly from a
///     feature that was configured incompletely.
/// </remarks>
public sealed class ModuleCompositionException : LiteBusConfigurationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ModuleCompositionException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    public ModuleCompositionException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModuleCompositionException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    /// <param name="innerException">The exception that caused this configuration failure.</param>
    public ModuleCompositionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
