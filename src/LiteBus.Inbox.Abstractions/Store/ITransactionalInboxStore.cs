namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Marks an inbox writer that participates in the caller's active transaction boundary.
/// </summary>
/// <remarks>
///     Bound instances write through the caller's open transaction and do not commit independently. Entity Framework Core
///     storage binds through a shared <c>DbContext</c> and optional save-changes interceptor registration. PostgreSQL and
///     other ADO.NET stores bind through an open connection and transaction supplied by the application.
/// </remarks>
public interface ITransactionalInboxStore : IInboxStore
{
}