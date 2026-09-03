using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Reads a message's pipeline plan out of the registry.
/// </summary>
/// <remarks>
///     <para>
///         Call it from a unit test, a startup log line, or a management endpoint. It is the traversal a command-line
///         tool or a generated plan would front, so those become presentation over one implementation rather than
///         second implementations that can disagree with the runtime.
///     </para>
///     <para>
///         The ordering rules it reproduces are the pipeline's own. Every role but completion runs handlers registered
///         for the message before handlers registered for a base type, and orders within each group by priority and
///         then registration sequence. Completion orders by priority alone across both groups, because a completion
///         handler observes an ending rather than wrapping the handler, and that order decides whether an audit record
///         lands inside the transaction.
///     </para>
/// </remarks>
public static class MessagePipelineExplainer
{
    /// <summary>
    ///     Describes everything registered for a message, in the order it will run.
    /// </summary>
    /// <param name="reader">The registry to read.</param>
    /// <param name="messageType">The concrete message type to explain.</param>
    /// <returns>The plan, with no steps when nothing is registered for the message.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader" /> or <paramref name="messageType" /> is <see langword="null" />.
    /// </exception>
    public static MessagePipelinePlan Explain(this IMessageReader reader, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(messageType);

        var descriptor = reader.Find(messageType);

        if (descriptor is null)
        {
            return new MessagePipelinePlan
            {
                MessageType = messageType,
                MessageResultType = FindResultType(messageType),
                Steps = []
            };
        }

        var steps = new List<MessagePipelineStep>();

        foreach (var stage in PipelineContracts.StagesInOrder)
        {
            steps.AddRange(Describe(
                StageName(stage),
                descriptor.PreStageHandlers.Where(handler => handler.Stage == stage),
                descriptor.IndirectPreStageHandlers.Where(handler => handler.Stage == stage)));
        }

        steps.AddRange(Describe("main", descriptor.Handlers, descriptor.IndirectHandlers));
        steps.AddRange(Describe("post", descriptor.PostHandlers, descriptor.IndirectPostHandlers));
        steps.AddRange(Describe("error", descriptor.ErrorHandlers, descriptor.IndirectErrorHandlers));
        steps.AddRange(Describe("refusal", descriptor.RefusalMappers, descriptor.IndirectRefusalMappers));

        // Completion is the one role ordered by priority alone across the direct and indirect split, so it cannot be
        // described by the same helper.
        steps.AddRange(descriptor.CompletionHandlers
            .Select(handler => (Handler: handler, Indirect: false))
            .Concat(descriptor.IndirectCompletionHandlers.Select(handler => (Handler: handler, Indirect: true)))
            .OrderBy(entry => entry.Handler.Priority)
            .ThenBy(entry => entry.Handler.RegistrationSequence)
            .Select(entry => ToStep("completion", entry.Handler, entry.Indirect)));

        return new MessagePipelinePlan
        {
            MessageType = messageType,
            MessageResultType = FindResultType(messageType),
            Steps = steps
        };
    }

    /// <summary>
    ///     Describes one role, indirect handlers before direct ones.
    /// </summary>
    /// <typeparam name="TDescriptor">The descriptor kind for the role.</typeparam>
    /// <param name="stage">The stage name written to each step.</param>
    /// <param name="direct">The handlers registered for the message itself.</param>
    /// <param name="indirect">The handlers registered for a base type or marker interface.</param>
    /// <returns>The steps for that role, in the order the pipeline runs them.</returns>
    /// <remarks>
    ///     Indirect first, matching the rule that a broadly registered cross-cutting concern wraps a message-specific
    ///     one.
    /// </remarks>
    private static IEnumerable<MessagePipelineStep> Describe<TDescriptor>(
        string stage,
        IEnumerable<TDescriptor> direct,
        IEnumerable<TDescriptor> indirect)
        where TDescriptor : IHandlerDescriptor
    {
        return Ordered(indirect).Select(handler => ToStep(stage, handler, isIndirect: true))
            .Concat(Ordered(direct).Select(handler => ToStep(stage, handler, isIndirect: false)));
    }

    /// <summary>
    ///     Orders one group of handlers the way the pipeline does.
    /// </summary>
    /// <typeparam name="TDescriptor">The descriptor kind for the role.</typeparam>
    /// <param name="handlers">The handlers to order.</param>
    /// <returns>The handlers ordered by priority and then registration sequence.</returns>
    private static IOrderedEnumerable<TDescriptor> Ordered<TDescriptor>(IEnumerable<TDescriptor> handlers)
        where TDescriptor : IHandlerDescriptor
    {
        return handlers.OrderBy(handler => handler.Priority).ThenBy(handler => handler.RegistrationSequence);
    }

    /// <summary>
    ///     Converts one descriptor into a plan step.
    /// </summary>
    /// <param name="stage">The stage name.</param>
    /// <param name="handler">The descriptor to describe.</param>
    /// <param name="isIndirect">Whether the handler was registered for a base type.</param>
    /// <returns>The step.</returns>
    private static MessagePipelineStep ToStep(string stage, IHandlerDescriptor handler, bool isIndirect)
    {
        return new MessagePipelineStep(
            stage,
            handler.Priority,
            handler.HandlerType,
            handler.ContractType,
            isIndirect,
            handler.HandlerType.IsGenericType && !handler.HandlerType.IsGenericTypeDefinition);
    }

    /// <summary>
    ///     Names a pre stage for the plan.
    /// </summary>
    /// <param name="stage">The stage to name.</param>
    /// <returns>The lower-case stage name.</returns>
    /// <remarks>
    ///     Lower-cased from the enum rather than hand-written, so a new stage appears in the plan without this being
    ///     edited.
    /// </remarks>
    private static string StageName(PreStage stage)
    {
        return stage.ToString().ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Reads the result type a message declares.
    /// </summary>
    /// <param name="messageType">The concrete message type.</param>
    /// <returns>The declared result type, or <see langword="null" /> when the message declares none.</returns>
    private static Type? FindResultType(Type messageType)
    {
        foreach (var contract in messageType.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IProducesResult<>))
            {
                return contract.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
