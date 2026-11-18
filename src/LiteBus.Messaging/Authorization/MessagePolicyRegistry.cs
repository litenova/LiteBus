using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LiteBus.Messaging.Abstractions.Authorization;

namespace LiteBus.Messaging.Authorization;

/// <summary>
///     The named authorization policies one composition registered, read by the policy guard at mediation time.
/// </summary>
/// <remarks>
///     <para>
///         Built once when the message module builds from what <c>AddAuthorizationPolicies</c> collected, and
///         registered as a singleton, so every mediation reads the same names. Names compare ordinally: a policy is
///         vocabulary the composition owns, and two names differing only by case are two policies.
///     </para>
/// </remarks>
public sealed class MessagePolicyRegistry
{
    /// <summary>
    ///     The registered policies by name, compared ordinally.
    /// </summary>
    private readonly Dictionary<string, MessagePolicy> _policies;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessagePolicyRegistry" /> class.
    /// </summary>
    /// <param name="policies">The named policies the composition registered.</param>
    internal MessagePolicyRegistry(IReadOnlyDictionary<string, MessagePolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        _policies = new Dictionary<string, MessagePolicy>(policies, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Gets the registered policies by name.
    /// </summary>
    public IReadOnlyDictionary<string, MessagePolicy> Policies => _policies;

    /// <summary>
    ///     Reads the policy registered under a name.
    /// </summary>
    /// <param name="name">The policy name the message declares.</param>
    /// <param name="policy">The registered policy, or <see langword="null" /> when no policy carries the name.</param>
    /// <returns><see langword="true" /> when a policy is registered under <paramref name="name" />.</returns>
    public bool TryGetPolicy(string name, [NotNullWhen(true)] out MessagePolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _policies.TryGetValue(name, out policy);
    }
}
