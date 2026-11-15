namespace LiteBus.Commands.Abstractions;

/// <summary>
///     Represents the settings that control one command batch.
/// </summary>
public sealed class CommandBatchSettings
{
    /// <summary>
    ///     Gets or initializes the mediation settings applied to every command in the batch.
    /// </summary>
    /// <remarks>
    ///     Each command receives its own copy of <see cref="CommandMediationSettings.Items" />, so one command's
    ///     pipeline never sees the items another command's pipeline wrote.
    /// </remarks>
    public CommandMediationSettings Mediation { get; init; } = new();
}
