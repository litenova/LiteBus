namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Marks an inbox writer that can participate in an existing Entity Framework Core unit of work.
/// </summary>
/// <remarks>
///     Bound instances stage inserts until the caller invokes <c>SaveChanges</c> on the active database context.
///     Entity Framework Core storage exposes context binding through its concrete store and optional save-changes
///     interceptor registration.
/// </remarks>
public interface ITransactionalInboxStore : IInboxStore
{
}
