namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     A part of the application, usually one assembly of a modular monolith, that contributes its own LiteBus
///     configuration: handlers, contracts, outbox registrations and recurring jobs.
/// </summary>
/// <remarks>
///     <para>
///         Each application module configures the same LiteBus modules the host composes, through the same
///         <c>AddCommands</c>, <c>AddEvents</c>, <c>AddOutbox</c> and similar calls. When a LiteBus module is already
///         registered, a later call configures that module's builder again instead of registering a second module, so
///         every application module adds to one composition.
///     </para>
///     <para>
///         Register application modules with <c>AddApplicationModule&lt;TModule&gt;()</c> or discover them with
///         <c>AddApplicationModulesFromAssembly(assembly)</c>. Implementations need a parameterless constructor.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class LedgerModule : IApplicationModule
/// {
///     public void Configure(ILiteBusBuilder liteBus)
///     {
///         liteBus.AddCommands(commands => commands.RegisterFromAssembly(typeof(LedgerModule).Assembly));
///         liteBus.AddOutbox(outbox => outbox.Contracts.Register<EntryPosted>("ledger.entry-posted"));
///     }
/// }
/// ]]></code>
/// </example>
public interface IApplicationModule
{
    /// <summary>
    ///     Adds this application module's configuration to the LiteBus composition.
    /// </summary>
    /// <param name="liteBus">The LiteBus builder the host composes.</param>
    void Configure(ILiteBusBuilder liteBus);
}
