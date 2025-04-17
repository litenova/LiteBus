using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Attribute to tag a handler with a specific operational context or scenario.
///     This helps in selecting the appropriate handler based on the context in which a command or query is executed.
/// </summary>
/// <example>
///     This example shows how to use the HandlerTagAttribute:
///     <code><![CDATA[
/// [HandlerTag("FrontEnd")]
/// public class CreateProductCommandValidator : ICommandPreHandler<CreateProductCommand>
/// {
///     // validator implementation for frontend-specific validations
/// }
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class HandlerTagAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the HandlerTagAttribute class with a specified tag.
    /// </summary>
    /// <param name="tag">The tag representing the operational context or scenario.</param>
    public HandlerTagAttribute(string tag)
    {
        Tag = tag;
    }

    /// <summary>
    ///     Gets the tag associated with this attribute.
    /// </summary>
    public string Tag { get; }
}