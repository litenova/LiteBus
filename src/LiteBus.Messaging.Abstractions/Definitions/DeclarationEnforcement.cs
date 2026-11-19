namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Decides what happens when a registered message states no position on a required declaration.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Fail" /> is the default: composition throws, naming every offender. It is the end state, when
///         every message the requirement covers already declares its position or records an exemption.
///     </para>
///     <para>
///         <see cref="Report" /> is the gradual path there: composition succeeds and the
///         <c>litebus.declarations</c> diagnostic probe lists what remains, so a team adopts positions message by
///         message instead of landing the requirement atomically with the last declaration. Change the requirement to
///         <see cref="Fail" /> once the probe reports nothing.
///     </para>
/// </remarks>
public enum DeclarationEnforcement
{
    /// <summary>
    ///     Composition fails, naming every message in scope that states no position.
    /// </summary>
    Fail = 0,

    /// <summary>
    ///     Composition succeeds and the <c>litebus.declarations</c> diagnostic probe lists every message in scope that
    ///     states no position.
    /// </summary>
    Report = 1
}
