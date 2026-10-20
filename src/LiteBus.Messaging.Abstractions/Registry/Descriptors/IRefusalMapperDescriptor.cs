using System;
using System.ComponentModel;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes a refusal mapper registered for a message type and the result type it produces.
/// </summary>
/// <remarks>
///     A mapper is not a pipeline stage. It runs only on the refusal path, in place of raising, so it carries no
///     priority semantics of its own: the pipeline uses the mapper registered for the concrete message type when there
///     is one, and otherwise the first mapper registered for a base type or interface.
/// </remarks>
public interface IRefusalMapperDescriptor : IHandlerDescriptor
{
    /// <summary>
    ///     Gets the type of result this mapper produces, which is the type the refused caller receives.
    /// </summary>
    Type MessageResultType { get; }

    /// <summary>
    ///     Gets the dispatch bound to <see cref="IHandlerDescriptor.ContractType" /> during registration.
    /// </summary>
    /// <remarks>
    ///     This is a framework hook. It is <see langword="null" /> when the contract was still open at registration,
    ///     which happens for a mapper registered for a generic message; the pipeline binds those on first dispatch.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    PipelineDispatch? Dispatch { get; }
}
