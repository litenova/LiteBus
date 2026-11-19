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
    /// <remarks>
    ///     Only fail-mode requirements are checked here. Report-mode requirements are skipped: they list their
    ///     offenders through the <c>litebus.declarations</c> probe instead of failing. Each requirement reads through
    ///     the same catalog query the probe uses, so the two enforcements can never disagree about what is missing.
    /// </remarks>
    public static void Validate(IMessageReader reader, IReadOnlyList<DeclarationRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(requirements);

        var failing = requirements
            .Where(requirement => requirement.Enforcement == DeclarationEnforcement.Fail)
            .ToList();

        if (failing.Count == 0)
        {
            return;
        }

        var catalog = new MessageCatalog(reader);
        var undeclared = new Dictionary<DeclarationRequirement, List<Type>>();

        foreach (var requirement in failing)
        {
            var missing = ((IMessageCatalog) catalog)
                .MissingDeclaration(requirement.ValueType, requirement.Scope)
                .Select(entry => entry.MessageType)
                .ToList();

            if (missing.Count > 0)
            {
                undeclared[requirement] = missing;
            }
        }

        if (undeclared.Count > 0)
        {
            throw new MessageDeclarationException(BuildMessage(undeclared));
        }
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
