using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.EntityFrameworkCore.Stores;

/// <summary>
///     Chooses how caller-sized identifier lists are sent to the database in <c>Contains</c> filters.
/// </summary>
/// <remarks>
///     EF Core 10 sends a parameterized collection as one scalar parameter per element by default. SQL Server rejects
///     a command with more than 2,100 parameters, so a requeue or a message-id filter over a few thousand identifiers
///     that worked on EF Core 9 fails there. On SQL Server, the stores wrap such lists in
///     <see cref="EF.Parameter{T}(T)" />, which restores the single JSON parameter that EF Core 9 sent. Other
///     providers keep the EF Core 10 default, because their parameter limits are far above the batch sizes LiteBus
///     callers use.
/// </remarks>
internal static class EfCoreCollectionParameters
{
    /// <summary>
    ///     Gets a value indicating whether identifier lists should be sent as a single collection parameter.
    /// </summary>
    /// <param name="context">The database context that runs the query.</param>
    /// <returns>
    ///     <see langword="true" /> when the context runs on SQL Server; otherwise, <see langword="false" />.
    /// </returns>
    internal static bool UseSingleParameter(object context)
    {
        return context is DbContext dbContext
               && string.Equals(dbContext.Database.ProviderName, EfCoreRelationalProviderNames.SqlServer, StringComparison.Ordinal);
    }
}
