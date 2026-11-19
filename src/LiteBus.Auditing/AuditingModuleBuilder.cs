using System;
using System.Collections.Generic;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Auditing;

/// <summary>
///     Configures auditing: when audit records are captured, and whether they join a hash chain.
/// </summary>
/// <remarks>
///     Reached through <c>ModuleRegistryExtensions.AddAuditing</c>. A storage package adds its store with an extension
///     such as <c>UseMartenStore</c>; an anchor package adds its publisher and source with an extension on
///     <see cref="AuditHashChainBuilder" />. A store or anchor that needs settings registers its settings module as a
///     child through <see cref="RegisterChildModule" />, so the settings compose inside <c>AddAuditing</c>.
/// </remarks>
/// <example>
///     <code><![CDATA[
/// registry.AddAuditing(auditing => auditing
///     .Capture(AuditCapture.WithChange())
///     .UseHashChain(chain => chain
///         .CheckpointStaleAfter(TimeSpan.FromDays(1))));
/// ]]></code>
/// </example>
public sealed class AuditingModuleBuilder
{
    /// <summary>
    ///     The options this builder writes.
    /// </summary>
    private readonly AuditingOptions _options = new();

    /// <summary>
    ///     Child modules declared by storage and anchor packages composing through this builder.
    /// </summary>
    private readonly List<IModule> _childModules = [];

    /// <summary>
    ///     Gets the recorded options.
    /// </summary>
    internal AuditingOptions Options => _options;

    /// <summary>
    ///     Sets when and where audit records are written, relative to the business commit.
    /// </summary>
    /// <param name="capture">The capture mode. Defaults to <see cref="AuditCapture.WithChange" />.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     A message overrides this per message with an <see cref="AuditCaptureDeclaration" />. Composition picks
    ///     <see cref="AuditCapture.AfterCommitThroughOutbox" /> when the store cannot join the business
    ///     transaction, visibly in the composition model, and rejects an invalid capture and link pair with a
    ///     configuration exception.
    /// </remarks>
    public AuditingModuleBuilder Capture(AuditCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        _options.Capture = capture;

        return this;
    }

    /// <summary>
    ///     Chains captured records in a tamper-evident hash chain.
    /// </summary>
    /// <param name="configure">The chain configuration callback.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Calling this twice configures one chain instead of replacing it, so several application modules can add
    ///     to one composition.
    /// </remarks>
    public AuditingModuleBuilder UseHashChain(Action<AuditHashChainBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _options.Chain ??= new AuditHashChainOptions();
        configure(new AuditHashChainBuilder(_options.Chain, this));

        return this;
    }

    /// <summary>
    ///     Declares a child module owned by the auditing module.
    /// </summary>
    /// <param name="module">The child module to build alongside the auditing module.</param>
    /// <returns>The current <see cref="AuditingModuleBuilder" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The seam storage and anchor packages compose through, so a settings module is registered inside
    ///     <c>AddAuditing</c> rather than as a second top-level module the caller has to remember to add.
    /// </remarks>
    public AuditingModuleBuilder RegisterChildModule(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        _childModules.Add(module);
        return this;
    }

    /// <summary>
    ///     Collects the child modules declared on this builder.
    /// </summary>
    /// <returns>The child modules, in declaration order.</returns>
    public IReadOnlyList<IModule> CollectSubModules()
    {
        return _childModules;
    }
}
