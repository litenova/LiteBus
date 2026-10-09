using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Reads the loadable types from an assembly, skipping types the runtime cannot load.
/// </summary>
/// <remarks>
///     An assembly scan aborts the whole registration when one type cannot be loaded, so every module builder
///     reads through here instead of calling <see cref="Assembly.GetTypes" /> directly. Every scan site reaches
///     this helper through references its package already carries, so sharing the rule needs no new project
///     reference.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AssemblyTypeScanner
{
    /// <summary>
    ///     Gets the types the runtime could load from <paramref name="assembly" />.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The loadable types, in scan order, with unloadable entries removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Only <see cref="ReflectionTypeLoadException" /> is tolerated, because it is the one the loader
    ///     throws for partially loadable assemblies and it carries the types that did load. Any other
    ///     exception propagates to the caller unchanged.
    /// </remarks>
    public static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            List<Type> loadable = [];

            foreach (var type in exception.Types ?? [])
            {
                if (type is not null)
                {
                    loadable.Add(type);
                }
            }

            return loadable;
        }
    }
}
