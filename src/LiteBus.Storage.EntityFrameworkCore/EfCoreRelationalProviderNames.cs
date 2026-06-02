namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Well-known <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.ProviderName" /> values.
/// </summary>
public static class EfCoreRelationalProviderNames
{
    /// <summary>
    ///     The in-memory provider name.
    /// </summary>
    public const string InMemory = "Microsoft.EntityFrameworkCore.InMemory";

    /// <summary>
    ///     The Npgsql PostgreSQL provider name.
    /// </summary>
    public const string PostgreSql = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    ///     The Microsoft SQL Server provider name.
    /// </summary>
    public const string SqlServer = "Microsoft.EntityFrameworkCore.SqlServer";

    /// <summary>
    ///     The Pomelo MySQL provider name.
    /// </summary>
    public const string MySql = "Pomelo.EntityFrameworkCore.MySql";

    /// <summary>
    ///     The SQLite provider name.
    /// </summary>
    public const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";
}
