using System;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     Thrown when an <see cref="IDiagnosticCheck" /> reports a probe name that differs from its manifest descriptor.
/// </summary>
public sealed class DiagnosticCheckNameMismatchException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DiagnosticCheckNameMismatchException" /> class.
    /// </summary>
    /// <param name="implementationType">The diagnostic check implementation type.</param>
    /// <param name="descriptorName">The probe name recorded in the host manifest.</param>
    /// <param name="checkName">The probe name reported by the check instance.</param>
    public DiagnosticCheckNameMismatchException(Type implementationType, string descriptorName, string checkName)
        : base(
            $"Diagnostic check '{implementationType.FullName ?? implementationType.Name}' reported probe name '{checkName}', " +
            $"but the host manifest registered '{descriptorName}'. Align IDiagnosticCheck.Name with RegisterDiagnosticCheck.")
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(descriptorName);
        ArgumentNullException.ThrowIfNull(checkName);

        ImplementationType = implementationType;
        DescriptorName = descriptorName;
        CheckName = checkName;
    }

    /// <summary>
    ///     Gets the diagnostic check implementation type.
    /// </summary>
    public Type ImplementationType { get; }

    /// <summary>
    ///     Gets the probe name recorded in the host manifest.
    /// </summary>
    public string DescriptorName { get; }

    /// <summary>
    ///     Gets the probe name reported by the check instance.
    /// </summary>
    public string CheckName { get; }
}
