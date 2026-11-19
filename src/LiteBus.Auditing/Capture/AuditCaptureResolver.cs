using System;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Capture;

/// <summary>
///     Resolves the capture mode one message is captured with, and carries it from the pre-handler stage to the
///     completion stage that writes the record.
/// </summary>
/// <remarks>
///     <para>
///         The auditing builder sets the capture mode for every audited message. A message that needs a different
///         timing declares one with an <see cref="AuditCaptureDeclaration" /> through an
///         <c>IAuditCaptureDefinition&lt;TMessage&gt;</c> beside the message, and that declaration wins over the
///         builder default. There is no attribute form, because an <see cref="AuditCapture" /> is not an
///         attribute-compatible constant.
///     </para>
///     <para>
///         When the audit store cannot join the business transaction, a requested
///         <see cref="AuditCapture.WithChange" /> mode cannot commit with the change it describes, so composition
///         selects <see cref="AuditCapture.AfterCommitThroughOutbox" /> instead and reports the choice in the
///         composition model. A mode that already avoids the business transaction is left alone.
///     </para>
///     <para>
///         The record writer observes only the built record, whose message type name cannot be resolved back to a
///         type, so the mode is resolved while the message type is still known and handed forward in the mediation
///         data. A record written with no mediation in flight, or for a message the pre-handler stage never saw,
///         falls back to the builder default.
///     </para>
/// </remarks>
internal static class AuditCaptureResolver
{
    /// <summary>
    ///     Resolves the capture mode one message is captured with.
    /// </summary>
    /// <param name="messageType">The concrete runtime message type.</param>
    /// <param name="registry">The message registry holding the message declaration.</param>
    /// <param name="builderCapture">The capture mode the auditing builder set.</param>
    /// <param name="capabilities">
    ///     What the registered audit store can do, or <see langword="null" /> when no storage package was added.
    /// </param>
    /// <returns>The requested and effective capture modes for the message.</returns>
    /// <exception cref="AuditConfigurationException">
    ///     The builder default or the message override pairs an invalid capture and link.
    /// </exception>
    public static AuditCaptureResolution Resolve(
        Type messageType,
        IMessageReader registry,
        AuditCapture builderCapture,
        AuditStoreCapabilities? capabilities)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(builderCapture);

        AuditCaptureValidator.Validate(builderCapture, "the auditing builder");

        var requested = builderCapture;
        Type? overridingMessageType = null;

        if (FindOverride(messageType, registry) is { } overrideCapture)
        {
            AuditCaptureValidator.Validate(
                overrideCapture,
                $"message '{messageType.FullName ?? messageType.Name}'");
            requested = overrideCapture;
            overridingMessageType = messageType;
        }

        return ApplyStoreCapabilities(requested, overridingMessageType, capabilities);
    }

    /// <summary>
    ///     Resolves the capture mode messages without an override are captured with.
    /// </summary>
    /// <param name="builderCapture">The capture mode the auditing builder set.</param>
    /// <param name="capabilities">
    ///     What the registered audit store can do, or <see langword="null" /> when no storage package was added.
    /// </param>
    /// <returns>The requested and effective capture modes for the builder default.</returns>
    /// <exception cref="AuditConfigurationException">The builder default pairs an invalid capture and link.</exception>
    public static AuditCaptureResolution ResolveDefault(
        AuditCapture builderCapture,
        AuditStoreCapabilities? capabilities)
    {
        ArgumentNullException.ThrowIfNull(builderCapture);

        AuditCaptureValidator.Validate(builderCapture, "the auditing builder");

        return ApplyStoreCapabilities(builderCapture, null, capabilities);
    }

    /// <summary>
    ///     Hands the resolved capture mode to the completion stage through the mediation data.
    /// </summary>
    /// <param name="executionContext">The mediation the capture mode was resolved for.</param>
    /// <param name="capture">The resolved capture mode.</param>
    public static void StoreResolvedCapture(IExecutionContext executionContext, AuditCapture capture)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(capture);

        executionContext.Data.Set<AuditCapture>(capture);
    }

    /// <summary>
    ///     Reads the capture mode the pre-handler stage resolved for the mediation in flight, when there is one.
    /// </summary>
    /// <param name="executionContext">The mediation in flight, or <see langword="null" /> when there is none.</param>
    /// <returns>
    ///     The resolved capture mode, or <see langword="null" /> when no mediation is in flight or the pre-handler
    ///     stage resolved nothing for it. Callers fall back to the builder default.
    /// </returns>
    public static AuditCapture? ReadResolvedCapture(IExecutionContext? executionContext)
    {
        if (executionContext is null)
        {
            return null;
        }

        return executionContext.Data.TryGet<AuditCapture>(out var capture) ? capture : null;
    }

    /// <summary>
    ///     Selects the effective capture mode for a requested mode and the registered store.
    /// </summary>
    /// <param name="requested">The builder capture mode or the per-message override.</param>
    /// <param name="overridingMessageType">
    ///     The message whose declaration overrode the builder default, or <see langword="null" /> when no message
    ///     declares an override.
    /// </param>
    /// <param name="capabilities">
    ///     What the registered audit store can do, or <see langword="null" /> when no storage package was added.
    /// </param>
    /// <returns>The resolution, carrying the store adjustment when one applies.</returns>
    private static AuditCaptureResolution ApplyStoreCapabilities(
        AuditCapture requested,
        Type? overridingMessageType,
        AuditStoreCapabilities? capabilities)
    {
        if (requested is AuditCapture.WithChangeCapture && capabilities?.JoinsBusinessTransaction == false)
        {
            return new AuditCaptureResolution(
                requested,
                AuditCapture.AfterCommitThroughOutbox(),
                overridingMessageType,
                true);
        }

        return new AuditCaptureResolution(requested, requested, overridingMessageType, false);
    }

    /// <summary>
    ///     Reads the capture override declared for a message type, when the message declares one.
    /// </summary>
    /// <param name="messageType">The concrete runtime message type.</param>
    /// <param name="registry">The message registry holding the message declaration.</param>
    /// <returns>The override, or <see langword="null" /> when the message declares none.</returns>
    /// <remarks>
    ///     The declaration is read through the descriptor the mediation resolved: an unregistered runtime type
    ///     mediated through a base descriptor is captured under that descriptor's declaration, exactly as if the
    ///     type had been registered, so whether a mediation overrides the capture mode never depends on scan
    ///     coverage.
    /// </remarks>
    private static AuditCapture? FindOverride(Type messageType, IMessageReader registry)
    {
        var descriptor = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy().Find(messageType, registry);

        if (descriptor is null)
        {
            return null;
        }

        return descriptor.Metadata.TryGet<AuditCaptureDeclaration>(out var declaration)
            ? declaration.Capture
            : null;
    }
}
