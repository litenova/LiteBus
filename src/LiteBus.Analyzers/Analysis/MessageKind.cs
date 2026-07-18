namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Identifies LiteBus command and query message types declared in a compilation.
/// </summary>
internal enum MessageKind
{
    /// <summary>
    ///     Command messages implementing <c>LiteBus.Commands.Abstractions.ICommand</c>.
    /// </summary>
    Command,

    /// <summary>
    ///     Query messages implementing <c>LiteBus.Queries.Abstractions.IQuery</c> without stream semantics.
    /// </summary>
    Query,

    /// <summary>
    ///     Stream query messages implementing <c>LiteBus.Queries.Abstractions.IStreamQuery&lt;TResult&gt;</c>.
    /// </summary>
    StreamQuery
}