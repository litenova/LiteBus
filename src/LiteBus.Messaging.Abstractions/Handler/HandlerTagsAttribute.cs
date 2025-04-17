using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Attribute to tag a handler with multiple operational contexts or scenarios.
///     This is useful when a handler needs to be associated with more than one context or scenario.
/// </summary>
/// <example>
///     This example shows how to use the HandlerTagsAttribute:
///     <code>
/// <![CDATA[
/// [HandlerTags("FrontEnd", "ServiceBus")]
/// public class CreateProductCommandValidator : ICommandPreHandler<CreateProductCommand>
/// {
///     // validator implementation for frontend-specific validations
/// }
/// ]]>
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class HandlerTagsAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the HandlerTagsAttribute class with multiple tags.
    /// </summary>
    /// <param name="tags">An array of tags representing the operational contexts or scenarios.</param>
    public HandlerTagsAttribute(params string[] tags)
    {
        Tags = tags;
    }

    /// <summary>
    ///     Gets the tags associated with this attribute.
    /// </summary>
    public string[] Tags { get; }
}