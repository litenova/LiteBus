using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Fails composition when a registered message in a requirement's scope states no position on the required
///     metadata type.
/// </summary>
/// <remarks>
///     It runs after every module has been built, so the registry holds every message by the time it looks. The
///     messaging module that carries the requirements is foundational and builds first, which is why the check cannot
///     live in its own build step.
/// </remarks>
internal static class RequiredDeclarationValidator
{
    /// <summary>
    ///     Verifies that every registered message in scope declares each required value type or records an exemption.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <param name="requirements">The requirements collected from every module builder.</param>
    /// <exception cref="MessageDeclarationException">
    ///     One or more registered messages state no position on a required value type. The message names every
    ///     offender and the scope of the requirement it violated.
    /// </exception>
    public static void Validate(IMessageReader reader, IReadOnlyList<DeclarationRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(requirements);

        if (requirements.Count == 0)
        {
            return;
        }

        var undeclared = new Dictionary<DeclarationRequirement, List<Type>>();

        foreach (var descriptor in reader)
        {
            // Abstract types and interfaces are shapes rather than messages, and a declaration written for one covers
            // every message beneath it, so requiring them to declare anything themselves would be noise.
            if (descriptor.MessageType.IsAbstract || descriptor.MessageType.IsInterface)
            {
                continue;
            }

            descriptor.Metadata.TryGet<DeclarationExemptions>(out var exemptions);

            foreach (var requirement in requirements)
            {
                if (!requirement.Covers(descriptor.MessageType))
                {
                    continue;
                }

                if (Declares(descriptor.Metadata, requirement.ValueType) ||
                    exemptions?.Covers(requirement.ValueType) == true)
                {
                    continue;
                }

                if (!undeclared.TryGetValue(requirement, out var offenders))
                {
                    offenders = [];
                    undeclared[requirement] = offenders;
                }

                offenders.Add(descriptor.MessageType);
            }
        }

        if (undeclared.Count > 0)
        {
            throw new MessageDeclarationException(BuildMessage(undeclared));
        }
    }

    /// <summary>
    ///     Determines whether the metadata holds a value of the required type.
    /// </summary>
    /// <param name="metadata">The metadata resolved for one message type.</param>
    /// <param name="required">The required metadata value type.</param>
    /// <returns><see langword="true" /> when a value of that type is present.</returns>
    /// <remarks>
    ///     <see cref="IMessageMetadata.Contains{TValue}" /> is generic and the required type is only known at runtime,
    ///     so presence is decided by scanning the resolved values. There are a handful per message and this runs once
    ///     at composition, so the cost is irrelevant next to reflecting the generic call into place.
    /// </remarks>
    private static bool Declares(IMessageMetadata metadata, Type required)
    {
        return metadata.Values.Any(required.IsInstanceOfType);
    }

    /// <summary>
    ///     Builds the composition error, listing every message missing each requirement alongside that requirement's
    ///     scope.
    /// </summary>
    /// <param name="undeclared">The offending message types grouped by the requirement they violate.</param>
    /// <returns>The exception message.</returns>
    /// <remarks>
    ///     Every offender is named rather than only the first. A requirement turned on for an existing codebase reports
    ///     dozens at once, and fixing them one composition failure at a time would make the feature unusable. The scope
    ///     is named with them, so the line reads as the policy that was violated rather than as a list of types.
    /// </remarks>
    private static string BuildMessage(Dictionary<DeclarationRequirement, List<Type>> undeclared)
    {
        var lines = undeclared
            .OrderBy(entry => entry.Key.ValueType.Name, StringComparer.Ordinal)
            .Select(entry =>
                $"  {entry.Key.ValueType.Name} is required of {entry.Key.ScopeDescription} but is not declared by: "
                + string.Join(", ", entry.Value.Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal)));

        return "One or more registered messages state no position on a required declaration:"
               + Environment.NewLine
               + string.Join(Environment.NewLine, lines)
               + Environment.NewLine
               + "Declare the value with an attribute or a definition class, or record why the message does not need it "
               + "with [DeclarationExempt(typeof(TValue), \"rationale\")]. Narrow the requirement instead if the "
               + "messages listed were never meant to be in its scope.";
    }
}
