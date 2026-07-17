using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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
    public static string GetPayloadColumnType(EfCoreStorageProvider provider)
    {
        _ = provider;
        return "TEXT";
    }

    /// <summary>
    ///     Gets the store column type for optional distributed trace context JSON.
    /// </summary>
    /// <param name="provider">The target storage provider.</param>
    /// <returns>The provider-specific column type.</returns>
    public static string GetTraceContextColumnType(EfCoreStorageProvider provider)
    {
        return GetPayloadColumnType(provider);
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
