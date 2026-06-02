namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Identifies the Entity Framework Core relational provider used for leasing and model defaults.
/// </summary>
public enum EfCoreStorageProvider
{
    /// <summary>
    ///     The Entity Framework Core in-memory database provider.
    /// </summary>
    InMemory = 0,

    /// <summary>
    ///     PostgreSQL through <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>.
    /// </summary>
    PostgreSql = 1,

    /// <summary>
    ///     Microsoft SQL Server through <c>Microsoft.EntityFrameworkCore.SqlServer</c>.
    /// </summary>
    SqlServer = 2,

    /// <summary>
    ///     MySQL or MariaDB through <c>Pomelo.EntityFrameworkCore.MySql</c>.
    /// </summary>
    MySql = 3,

    /// <summary>
    ///     SQLite through <c>Microsoft.EntityFrameworkCore.Sqlite</c>.
    /// </summary>
    Sqlite = 4
}
