namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Identifies LiteBus command and query message types declared in a compilation.
/// </summary>
internal enum MessageKind
{
    /// <summary>
    ///     Command messages implementing <see cref="LiteBus.Commands.Abstractions.ICommand" />.
    /// </summary>
    Command,

    /// <summary>
    ///     Query messages implementing <see cref="LiteBus.Queries.Abstractions.IQuery" />.
    /// </summary>
    Query
}