using LiteBus.Commands.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Marks a command as intended for inbox scheduling.
/// </summary>
/// <remarks>
///     <para>
///         This marker is optional. <see cref="ICommandScheduler" /> accepts any <see cref="ICommand" /> because the API
///         call itself is the inbox boundary. Use this marker in application code when teams want type names or generic
///         constraints to make inbox commands visible during review.
///     </para>
/// </remarks>
public interface IInboxCommand : ICommand;