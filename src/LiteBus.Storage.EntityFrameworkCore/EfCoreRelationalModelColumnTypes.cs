using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LiteBus.Storage.EntityFrameworkCore.Exceptions;

namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Maps JSON payload and trace columns to provider-specific store types.
/// </summary>
public static class EfCoreRelationalModelColumnTypes
{
    /// <summary>
    ///     Gets the store column type for serialized JSON payloads.
    /// </summary>
    /// <param name="provider">The target storage provider.</param>
    /// <returns>The provider-specific column type.</returns>
    /// <remarks>
    ///     The payload is stored as unbounded Unicode text rather than a JSON type, because a payload encryptor or a
    ///     custom serializer may write content that is not JSON. On SQL Server that is <c>nvarchar(max)</c>, because
    ///     <c>text</c> is a deprecated non-Unicode type that loses characters outside the database code page. On
    ///     MySQL it is <c>longtext</c>, because <c>text</c> holds at most 64 KB.
    /// </remarks>
    /// <exception cref="EfCoreStorageNotSupportedException">Thrown when no column type is defined for the provider.</exception>
    public static string GetPayloadColumnType(EfCoreStorageProvider provider)
    {
        return provider switch
        {
            EfCoreStorageProvider.PostgreSql => "TEXT",
            EfCoreStorageProvider.SqlServer => "nvarchar(max)",
            EfCoreStorageProvider.MySql => "longtext",
            EfCoreStorageProvider.Sqlite => "TEXT",
            EfCoreStorageProvider.InMemory => "TEXT",
            _ => throw new EfCoreStorageNotSupportedException($"Payload column types are not defined for provider '{provider}'.")
        };
    }

    /// <summary>
    ///     Gets the store column type for optional distributed trace context JSON.
    /// </summary>
    /// <param name="provider">The target storage provider.</param>
    /// <returns>The provider-specific column type.</returns>
    public static string GetTraceContextColumnType(EfCoreStorageProvider provider)
    {
        return provider switch
        {
            EfCoreStorageProvider.PostgreSql => "jsonb",
            EfCoreStorageProvider.SqlServer => "nvarchar(max)",
            EfCoreStorageProvider.MySql => "json",
            EfCoreStorageProvider.Sqlite => "TEXT",
            EfCoreStorageProvider.InMemory => "TEXT",
            _ => throw new EfCoreStorageNotSupportedException($"Trace context column types are not defined for provider '{provider}'.")
        };
    }

    /// <summary>
    ///     Configures a string property as a JSON payload column when a provider is supplied.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="property">The property builder.</param>
    /// <param name="provider">
    ///     The target provider; when <see langword="null" />, no store-specific column type is applied.
    /// </param>
    /// <returns>The same property builder for chaining.</returns>
    public static PropertyBuilder<string> ConfigureJsonPayloadColumn<TEntity>(
        this PropertyBuilder<string> property,
        EfCoreStorageProvider? provider)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(property);

        if (provider is null)
        {
            return property;
        }

        return property.HasColumnType(GetPayloadColumnType(provider.Value));
    }

    /// <summary>
    ///     Configures a nullable string property as optional JSON metadata when a provider is supplied.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="property">The property builder.</param>
    /// <param name="provider">
    ///     The target provider; when <see langword="null" />, no store-specific column type is applied.
    /// </param>
    /// <returns>The same property builder for chaining.</returns>
    public static PropertyBuilder<string?> ConfigureJsonTraceContextColumn<TEntity>(
        this PropertyBuilder<string?> property,
        EfCoreStorageProvider? provider)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(property);

        if (provider is null)
        {
            return property;
        }

        return property.HasColumnType(GetTraceContextColumnType(provider.Value));
    }
}
