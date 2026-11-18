using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime;

/// <summary>
///     Provides extension methods for <see cref="ILiteBusBuilder" /> to register application modules.
/// </summary>
public static class ApplicationModuleExtensions
{
    /// <summary>
    ///     Adds an application module to the LiteBus composition.
    /// </summary>
    /// <typeparam name="TModule">The application module type, created with its parameterless constructor.</typeparam>
    /// <param name="builder">The package-neutral LiteBus builder.</param>
    /// <returns>The current LiteBus builder.</returns>
    public static ILiteBusBuilder AddApplicationModule<TModule>(this ILiteBusBuilder builder)
        where TModule : IApplicationModule, new()
    {
        ArgumentNullException.ThrowIfNull(builder);

        var module = new TModule();
        module.Configure(builder);
        return builder;
    }

    /// <summary>
    ///     Adds an application module instance to the LiteBus composition.
    /// </summary>
    /// <param name="builder">The package-neutral LiteBus builder.</param>
    /// <param name="module">The application module whose configuration is applied.</param>
    /// <returns>The current LiteBus builder.</returns>
    public static ILiteBusBuilder AddApplicationModule(this ILiteBusBuilder builder, IApplicationModule module)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(module);

        module.Configure(builder);
        return builder;
    }

    /// <summary>
    ///     Discovers and adds every application module in an assembly, in type-name order.
    /// </summary>
    /// <param name="builder">The package-neutral LiteBus builder.</param>
    /// <param name="assembly">The assembly to scan for application modules.</param>
    /// <returns>The current LiteBus builder.</returns>
    /// <remarks>
    ///     Every non-abstract public or internal class implementing <see cref="IApplicationModule" /> with a
    ///     parameterless constructor is created and configured, ordered by full type name. Abstract classes, private
    ///     types and types without a parameterless constructor are skipped.
    /// </remarks>
    public static ILiteBusBuilder AddApplicationModulesFromAssembly(this ILiteBusBuilder builder, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var moduleType in CollectApplicationModuleTypes(assembly))
        {
            var module = (IApplicationModule) Activator.CreateInstance(moduleType, nonPublic: true)!;
            module.Configure(builder);
        }

        return builder;
    }

    /// <summary>
    ///     Collects the application module types in an assembly, in type-name order.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The module types, ordered by full type name.</returns>
    private static List<Type> CollectApplicationModuleTypes(Assembly assembly)
    {
        Type[] candidates;

        try
        {
            candidates = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            candidates = (exception.Types ?? []).OfType<Type>().ToArray();
        }

        return candidates
            .Where(IsApplicationModuleCandidate)
            .OrderBy(static type => type.FullName ?? type.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Determines whether a type is a creatable application module.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true" /> when the type is a non-abstract public or internal class with a parameterless constructor; otherwise, <see langword="false" />.</returns>
    private static bool IsApplicationModuleCandidate(Type type)
    {
        if (!type.IsClass || type.IsAbstract)
        {
            return false;
        }

        if (!typeof(IApplicationModule).IsAssignableFrom(type))
        {
            return false;
        }

        if (!IsPublicOrInternal(type))
        {
            return false;
        }

        return type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null) is not null;
    }

    /// <summary>
    ///     Determines whether a type is public or internal, including nested public and nested internal types.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true" /> when the type is public or internal; otherwise, <see langword="false" />.</returns>
    private static bool IsPublicOrInternal(Type type)
    {
        return type.IsPublic || type.IsNotPublic || type.IsNestedPublic || type.IsNestedAssembly;
    }
}
