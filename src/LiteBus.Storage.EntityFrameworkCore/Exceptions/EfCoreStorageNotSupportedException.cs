namespace LiteBus.Storage.EntityFrameworkCore.Exceptions;

/// <summary>
///     Thrown when an Entity Framework Core storage helper does not support the current database provider.
/// </summary>
public sealed class EfCoreStorageNotSupportedException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreStorageNotSupportedException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public EfCoreStorageNotSupportedException(string message)
        : base(message)
    {
    }
}