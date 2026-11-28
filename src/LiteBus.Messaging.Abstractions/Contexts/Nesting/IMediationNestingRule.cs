namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Decides whether a message may be mediated inside another mediation.
/// </summary>
/// <remarks>
///     <para>
///         The mediator evaluates every registered rule before a nested mediation starts: when the ambient execution
///         context is not <see langword="null" />, it resolves the registered rules from the dispatch scope and
///         evaluates each in registration order. The first denial throws <see cref="NestedMediationException" />. A
///         root mediation, which has no enclosing context, skips rule resolution entirely, so a composition without
///         nesting rules pays nothing for them.
///     </para>
///     <para>
///         A rule reads the chain through <see cref="MediationNestingContext.Ancestors" />, which walks from the direct
///         parent up to the root. The built-in command rule, registered through
///         <c>CommandModuleBuilder.ForbidNestedCommands</c>, denies a command nested anywhere under another command. An
///         application adds its own rules, such as an exemption that allows a named inner message under a named outer
///         one, by registering them for collection resolution.
///     </para>
/// </remarks>
public interface IMediationNestingRule
{
    /// <summary>
    ///     Evaluates the nested mediation described by the context.
    /// </summary>
    /// <param name="context">The nested mediation: the enclosing context and the inner message type.</param>
    /// <returns>The verdict that allows or denies the nested mediation.</returns>
    Verdict Evaluate(MediationNestingContext context);
}
