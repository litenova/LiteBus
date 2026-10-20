namespace LiteBus.Messaging;

/// <summary>
///     One open generic handler and how many message types it was closed over.
/// </summary>
/// <param name="HandlerName">The open generic handler type name.</param>
/// <param name="MessageCount">The number of concrete message types the registry closed it over.</param>
/// <remarks>
///     A count of zero means the handler fits nothing that was registered, so it never runs. That is worth seeing:
///     nothing else reports it, because a handler covering no messages registers exactly as cleanly as one covering
///     every message.
/// </remarks>
public sealed record OpenGenericClosure(string HandlerName, int MessageCount);
