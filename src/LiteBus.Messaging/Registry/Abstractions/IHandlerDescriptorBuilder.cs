using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Abstractions;

/// <summary>
///     Builds handler descriptors from a handler CLR type.
/// </summary>
internal interface IHandlerDescriptorBuilder
{
    /// <summary>
    ///     Determines whether this builder can analyze the specified type.
    /// </summary>
    /// <param name="type">The candidate handler type.</param>
    /// <returns><see langword="true" /> when this builder can produce descriptors; otherwise, <see langword="false" />.</returns>
    bool CanBuild(Type type);

    /// <summary>
    ///     Creates handler descriptors discovered on the specified type.
    /// </summary>
    /// <param name="type">The handler type to analyze.</param>
    /// <returns>The descriptors produced for the type.</returns>
    IEnumerable<IHandlerDescriptor> Build(Type type);
}
