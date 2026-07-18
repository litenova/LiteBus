using LiteBus.Inbox.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Maps LiteBus operator management and diagnostic endpoints on ASP.NET Core hosts.
/// </summary>
public static partial class LiteBusManagementEndpointExtensions
{
    /// <summary>
    ///     Logs a request rejected because its input is invalid.
    /// </summary>
    private static readonly Action<ILogger, string, string, string, Exception?> InvalidManagementRequestMessage =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(4001, "InvalidManagementRequest"),
            "LiteBus management request {TraceIdentifier} was invalid for {RequestMethod} {RequestPath}.");

    /// <summary>
    ///     Logs a request rejected by an operator safety rule.
    /// </summary>
    private static readonly Action<ILogger, string, string, string, Exception?> ManagementSafetyRejectionMessage =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(4002, "ManagementSafetyRejection"),
            "LiteBus management request {TraceIdentifier} was rejected for {RequestMethod} {RequestPath}.");

    /// <summary>
    ///     Logs a management operation that failed unexpectedly.
    /// </summary>
    private static readonly Action<ILogger, string, string, string, Exception?> ManagementOperationFailedMessage =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            new EventId(5001, "ManagementOperationFailed"),
            "LiteBus management request {TraceIdentifier} failed for {RequestMethod} {RequestPath}.");

    /// <summary>
    ///     Maps inbox and outbox management endpoints when the matching manager is registered, or returns
    ///     <c>404 Not Found</c> for an axis that is not configured on the host.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="options">Optional route and authorization settings.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder AddLiteBusManagementEndpoints(
        this IEndpointRouteBuilder endpoints,
        LiteBusManagementOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        options ??= endpoints.ServiceProvider.GetService<LiteBusManagementOptions>() ?? new LiteBusManagementOptions();
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RoutePrefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxPageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBulkMessageIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.DefaultDrainTimeout.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxDrainTimeout.Ticks);
        ArgumentNullException.ThrowIfNull(options.DiagnosticChecks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.DiagnosticChecks.MaxParallelism);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.DiagnosticChecks.Timeout.Ticks);
        if (options.DefaultDrainTimeout > options.MaxDrainTimeout)
        {
            throw new ArgumentException("DefaultDrainTimeout cannot exceed MaxDrainTimeout.", nameof(options));
        }
        var prefix = options.RoutePrefix.Trim('/');
        var services = endpoints.ServiceProvider;
        var hasInbox = services.GetService(typeof(IInboxManager)) is not null;
        var hasOutbox = services.GetService(typeof(IOutboxManager)) is not null;

        var inboxGroup = ApplyManagementAuthorization(endpoints.MapGroup(BuildManagementRoute(prefix, "inbox")), options);
        var outboxGroup = ApplyManagementAuthorization(endpoints.MapGroup(BuildManagementRoute(prefix, "outbox")), options);

        if (hasInbox)
        {
            MapInboxManagementEndpoints(inboxGroup, options);
        }
        else
        {
            MapAxisNotConfigured(inboxGroup, "Inbox");
        }

        if (hasOutbox)
        {
            MapOutboxManagementEndpoints(outboxGroup, options);
        }
        else
        {
            MapAxisNotConfigured(outboxGroup, "Outbox");
        }

        ApplyManagementAuthorization(
            endpoints.MapGet(
                BuildManagementRoute(prefix, "health"),
                (LiteBusHostManifest manifest, IServiceProvider serviceProvider, CancellationToken cancellationToken) =>
                    RunDiagnosticChecksAsync(manifest, serviceProvider, options, cancellationToken)),
            options);

        return endpoints;
    }

    /// <summary>
    ///     Maps inbox management routes when <see cref="IInboxManager" /> is registered.
    /// </summary>
    /// <param name="inboxGroup">The inbox route group.</param>
    /// <param name="options">The management endpoint options.</param>
    private static void MapInboxManagementEndpoints(RouteGroupBuilder inboxGroup, LiteBusManagementOptions options)
    {
        inboxGroup.MapGet("/messages", (HttpContext context, IInboxManager manager, [AsParameters] InboxMessageQueryBinding parameters, CancellationToken cancellationToken) =>
            QueryInboxMessagesAsync(context, manager, parameters, options, cancellationToken));
        inboxGroup.MapGet("/messages/{messageId:guid}", GetInboxMessageAsync);
        inboxGroup.MapPost("/messages/requeue", (HttpContext context, IInboxManager manager, [FromBody] RequeueMessagesRequest request, CancellationToken cancellationToken) =>
            RequeueInboxMessagesAsync(context, manager, request, options, cancellationToken));
        inboxGroup.MapPost("/messages/requeue-dead-letters", RequeueInboxDeadLettersAsync);
        inboxGroup.MapDelete("/messages", (HttpContext context, IInboxManager manager, [AsParameters] InboxMessagePurgeBinding parameters, [FromBody] PurgeConfirmRequest? confirmRequest, CancellationToken cancellationToken) =>
            PurgeInboxMessagesAsync(context, manager, parameters, confirmRequest, options, cancellationToken));
        inboxGroup.MapGet("/status-counts", GetInboxStatusCountsAsync);
        inboxGroup.MapGet("/schema", GetInboxSchemaAsync);
        inboxGroup.MapGet("/retention/status", GetInboxRetentionStatusAsync);
        inboxGroup.MapPost("/retention/purge", RunInboxRetentionPurgeAsync);
        inboxGroup.MapGet("/processor/state", GetInboxProcessorStateAsync);
        inboxGroup.MapPost("/processor/pause", PauseInboxProcessorAsync);
        inboxGroup.MapPost("/processor/resume", ResumeInboxProcessorAsync);
        inboxGroup.MapPost("/processor/drain", DrainInboxProcessorAsync);
    }

    /// <summary>
    ///     Maps outbox management routes when <see cref="IOutboxManager" /> is registered.
    /// </summary>
    /// <param name="outboxGroup">The outbox route group.</param>
    /// <param name="options">The management endpoint options.</param>
    private static void MapOutboxManagementEndpoints(RouteGroupBuilder outboxGroup, LiteBusManagementOptions options)
    {
        outboxGroup.MapGet("/messages", (HttpContext context, IOutboxManager manager, [AsParameters] OutboxMessageQueryBinding parameters, CancellationToken cancellationToken) =>
            QueryOutboxMessagesAsync(context, manager, parameters, options, cancellationToken));
        outboxGroup.MapGet("/messages/{messageId:guid}", GetOutboxMessageAsync);
        outboxGroup.MapPost("/messages/requeue", (HttpContext context, IOutboxManager manager, [FromBody] RequeueMessagesRequest request, CancellationToken cancellationToken) =>
            RequeueOutboxMessagesAsync(context, manager, request, options, cancellationToken));
        outboxGroup.MapPost("/messages/requeue-dead-letters", RequeueOutboxDeadLettersAsync);
        outboxGroup.MapDelete("/messages", (HttpContext context, IOutboxManager manager, [AsParameters] OutboxMessagePurgeBinding parameters, [FromBody] PurgeConfirmRequest? confirmRequest, CancellationToken cancellationToken) =>
            PurgeOutboxMessagesAsync(context, manager, parameters, confirmRequest, options, cancellationToken));
        outboxGroup.MapGet("/status-counts", GetOutboxStatusCountsAsync);
        outboxGroup.MapGet("/schema", GetOutboxSchemaAsync);
        outboxGroup.MapGet("/retention/status", GetOutboxRetentionStatusAsync);
        outboxGroup.MapPost("/retention/purge", RunOutboxRetentionPurgeAsync);
        outboxGroup.MapGet("/processor/state", GetOutboxProcessorStateAsync);
        outboxGroup.MapPost("/processor/pause", PauseOutboxProcessorAsync);
        outboxGroup.MapPost("/processor/resume", ResumeOutboxProcessorAsync);
        outboxGroup.MapPost("/processor/drain", DrainOutboxProcessorAsync);
    }

    /// <summary>
    ///     Maps a fallback route that reports an axis is not configured on the host.
    /// </summary>
    /// <param name="group">The route group for the unconfigured axis.</param>
    /// <param name="axisName">The axis name included in the not-found response.</param>
    private static void MapAxisNotConfigured(RouteGroupBuilder group, string axisName)
    {
        group.MapFallback(() => Results.NotFound($"{axisName} is not configured."));
    }

    /// <summary>
    ///     Builds a management route template from an optional prefix and segment without consecutive slashes.
    /// </summary>
    /// <param name="prefix">The trimmed route prefix, or an empty string when routes are rooted at the application base.</param>
    /// <param name="segment">The route segment following the prefix.</param>
    /// <returns>A route template beginning with a single leading slash.</returns>
    private static string BuildManagementRoute(string prefix, string segment)
    {
        return string.IsNullOrEmpty(prefix) ? $"/{segment}" : $"/{prefix}/{segment}";
    }

    /// <summary>
    ///     Applies authorization metadata to a management route or group when anonymous access is not allowed.
    /// </summary>
    /// <typeparam name="TBuilder">The route convention builder type.</typeparam>
    /// <param name="builder">The route or group builder.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <returns>The builder with authorization metadata when required.</returns>
    private static TBuilder ApplyManagementAuthorization<TBuilder>(TBuilder builder, LiteBusManagementOptions options)
        where TBuilder : IEndpointConventionBuilder
    {
        if (options.AllowAnonymousManagement)
        {
            return builder;
        }

        if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
        {
            builder.RequireAuthorization(options.AuthorizationPolicy);
            return builder;
        }

        builder.RequireAuthorization();
        return builder;
    }

    /// <summary>
    ///     Queries inbox messages using the supplied filter and page request from the query string.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="parameters">The bound query string parameters.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching inbox message page.</returns>
    private static Task<IResult> QueryInboxMessagesAsync(
        HttpContext context,
        IInboxManager manager,
        [AsParameters] InboxMessageQueryBinding parameters,
        LiteBusManagementOptions options,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(
            await manager.QueryAsync(parameters.ToFilter(), parameters.ToPageRequest(options.MaxPageSize), cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns one inbox message by identifier.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching envelope or a not-found response.</returns>
    private static Task<IResult> GetInboxMessageAsync(
        HttpContext context,
        IInboxManager manager,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () =>
        {
            var message = await manager.GetMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
            return message is null ? Results.NotFound() : Results.Ok(message);
        });
    }

    /// <summary>
    ///     Requeues selected inbox messages by identifier.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="request">The message identifiers to requeue.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    private static Task<IResult> RequeueInboxMessagesAsync(
        HttpContext context,
        IInboxManager manager,
        [FromBody] RequeueMessagesRequest request,
        LiteBusManagementOptions options,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () =>
        {
            ValidateBulkRequest(request, options);
            return Results.Ok(await manager.RequeueAsync(request.MessageIds, cancellationToken).ConfigureAwait(false));
        });
    }

    /// <summary>
    ///     Requeues all dead-lettered inbox messages.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    private static Task<IResult> RequeueInboxDeadLettersAsync(
        HttpContext context,
        IInboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(await manager.RequeueDeadLettersAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Purges inbox messages that match the supplied filter.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="parameters">The bound query string parameters.</param>
    /// <param name="confirmRequest">The JSON body that confirms unrestricted purge.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    private static Task<IResult> PurgeInboxMessagesAsync(
        HttpContext context,
        IInboxManager manager,
        [AsParameters] InboxMessagePurgeBinding parameters,
        [FromBody] PurgeConfirmRequest? confirmRequest,
        LiteBusManagementOptions options,
        CancellationToken cancellationToken)
    {
        return ExecuteManagementAsync(context, async () => Results.Ok(
            await PurgeInboxWithValidationAsync(
                    manager,
                    parameters.ToFilter(),
                    confirmRequest?.Confirm ?? false,
                    parameters.MessageIds,
                    options,
                    cancellationToken)
                .ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns inbox status counts.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>Status counts grouped by <see cref="InboxStatus" />.</returns>
    private static Task<IResult> GetInboxStatusCountsAsync(
        HttpContext context,
        IInboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(await manager.GetStatusCountsAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns inbox schema version metadata.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>Expected and recorded schema versions.</returns>
    private static Task<IResult> GetInboxSchemaAsync(
        HttpContext context,
        IInboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(await manager.GetSchemaInfoAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns inbox retention cleanup status.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>The configured retention policy and most recent cleanup outcome.</returns>
    private static Task<IResult> GetInboxRetentionStatusAsync(
        HttpContext context,
        IInboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(await manager.GetRetentionStatusAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Triggers an immediate inbox retention purge.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of rows deleted.</returns>
    private static Task<IResult> RunInboxRetentionPurgeAsync(
        HttpContext context,
        IInboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(await manager.RunRetentionPurgeAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns the inbox processor loop state.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="services">The request services.</param>
    /// <returns>The current processor state or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> GetInboxProcessorStateAsync(HttpContext context, IServiceProvider services)
    {
        return ExecuteAsync(context, () =>
        {
            var control = services.GetService<IInboxProcessorControl>();

            return Task.FromResult(control is null
                ? Results.NotFound("Inbox processor is not enabled.")
                : Results.Ok(new ProcessorStateResponse { State = control.State }));
        });
    }

    /// <summary>
    ///     Pauses the inbox processor loop.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the gate.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> PauseInboxProcessorAsync(
        HttpContext context,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () =>
        {
            if (services.GetService<IInboxProcessorControl>() is not { } control)
            {
                return Results.NotFound("Inbox processor is not enabled.");
            }

            await control.PauseAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(new ProcessorStateResponse { State = control.State });
        });
    }

    /// <summary>
    ///     Resumes the inbox processor loop.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> ResumeInboxProcessorAsync(
        HttpContext context,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () =>
        {
            if (services.GetService<IInboxProcessorControl>() is not { } control)
            {
                return Results.NotFound("Inbox processor is not enabled.");
            }

            await control.ResumeAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(new ProcessorStateResponse { State = control.State });
        });
    }

    /// <summary>
    ///     Drains the inbox processor loop once and stops leasing.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="services">The request services.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="timeoutSeconds">The optional drain timeout override in seconds.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for drain completion.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> DrainInboxProcessorAsync(
        HttpContext context,
        IServiceProvider services,
        LiteBusManagementOptions options,
        [FromQuery] int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () =>
        {
            if (services.GetService<IInboxProcessorControl>() is not { } control)
            {
                return Results.NotFound("Inbox processor is not enabled.");
            }

            var timeout = timeoutSeconds is > 0
                ? TimeSpan.FromSeconds(timeoutSeconds.Value)
                : options.DefaultDrainTimeout;

            if (timeout > options.MaxDrainTimeout)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            }

            await control.DrainAsync(timeout, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new ProcessorStateResponse { State = control.State });
        });
    }

    /// <summary>
    ///     Queries outbox messages using the supplied filter and page request from the query string.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="parameters">The bound query string parameters.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching outbox message page.</returns>
    private static Task<IResult> QueryOutboxMessagesAsync(
        HttpContext context,
        IOutboxManager manager,
        [AsParameters] OutboxMessageQueryBinding parameters,
        LiteBusManagementOptions options,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(
            await manager.QueryAsync(parameters.ToFilter(), parameters.ToPageRequest(options.MaxPageSize), cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns one outbox message by identifier.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching envelope or a not-found response.</returns>
    private static Task<IResult> GetOutboxMessageAsync(
        HttpContext context,
        IOutboxManager manager,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () =>
        {
            var message = await manager.GetMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
            return message is null ? Results.NotFound() : Results.Ok(message);
        });
    }

    /// <summary>
    ///     Requeues selected outbox messages by identifier.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="request">The message identifiers to requeue.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    private static Task<IResult> RequeueOutboxMessagesAsync(
        HttpContext context,
        IOutboxManager manager,
        [FromBody] RequeueMessagesRequest request,
        LiteBusManagementOptions options,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () =>
        {
            ValidateBulkRequest(request, options);
            return Results.Ok(await manager.RequeueAsync(request.MessageIds, cancellationToken).ConfigureAwait(false));
        });
    }

    /// <summary>
    ///     Requeues all dead-lettered outbox messages.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    private static Task<IResult> RequeueOutboxDeadLettersAsync(
        HttpContext context,
        IOutboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(await manager.RequeueDeadLettersAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Purges outbox messages that match the supplied filter.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="parameters">The bound query string parameters.</param>
    /// <param name="confirmRequest">The JSON body that confirms unrestricted purge.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    private static Task<IResult> PurgeOutboxMessagesAsync(
        HttpContext context,
        IOutboxManager manager,
        [AsParameters] OutboxMessagePurgeBinding parameters,
        [FromBody] PurgeConfirmRequest? confirmRequest,
        LiteBusManagementOptions options,
        CancellationToken cancellationToken)
    {
        return ExecuteManagementAsync(context, async () => Results.Ok(
            await PurgeOutboxWithValidationAsync(
                    manager,
                    parameters.ToFilter(),
                    confirmRequest?.Confirm ?? false,
                    parameters.MessageIds,
                    options,
                    cancellationToken)
                .ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns outbox status counts.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>Status counts grouped by <see cref="OutboxStatus" />.</returns>
    private static Task<IResult> GetOutboxStatusCountsAsync(
        HttpContext context,
        IOutboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(await manager.GetStatusCountsAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns outbox schema version metadata.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>Expected and recorded schema versions.</returns>
    private static Task<IResult> GetOutboxSchemaAsync(
        HttpContext context,
        IOutboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(await manager.GetSchemaInfoAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns outbox retention cleanup status.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>The configured retention policy and most recent cleanup outcome.</returns>
    private static Task<IResult> GetOutboxRetentionStatusAsync(
        HttpContext context,
        IOutboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(await manager.GetRetentionStatusAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Triggers an immediate outbox retention purge.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of rows deleted.</returns>
    private static Task<IResult> RunOutboxRetentionPurgeAsync(
        HttpContext context,
        IOutboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () => Results.Ok(await manager.RunRetentionPurgeAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns the outbox processor loop state.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="services">The request services.</param>
    /// <returns>The current processor state or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> GetOutboxProcessorStateAsync(HttpContext context, IServiceProvider services)
    {
        return ExecuteAsync(context, () =>
        {
            var control = services.GetService<IOutboxProcessorControl>();

            return Task.FromResult(control is null
                ? Results.NotFound("Outbox processor is not enabled.")
                : Results.Ok(new OutboxProcessorStateResponse { State = control.State }));
        });
    }

    /// <summary>
    ///     Pauses the outbox processor loop.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the gate.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> PauseOutboxProcessorAsync(
        HttpContext context,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () =>
        {
            if (services.GetService<IOutboxProcessorControl>() is not { } control)
            {
                return Results.NotFound("Outbox processor is not enabled.");
            }

            await control.PauseAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(new OutboxProcessorStateResponse { State = control.State });
        });
    }

    /// <summary>
    ///     Resumes the outbox processor loop.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> ResumeOutboxProcessorAsync(
        HttpContext context,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () =>
        {
            if (services.GetService<IOutboxProcessorControl>() is not { } control)
            {
                return Results.NotFound("Outbox processor is not enabled.");
            }

            await control.ResumeAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(new OutboxProcessorStateResponse { State = control.State });
        });
    }

    /// <summary>
    ///     Drains the outbox processor loop once and stops leasing.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="services">The request services.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="timeoutSeconds">The optional drain timeout override in seconds.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for drain completion.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> DrainOutboxProcessorAsync(
        HttpContext context,
        IServiceProvider services,
        LiteBusManagementOptions options,
        [FromQuery] int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(context, async () =>
        {
            if (services.GetService<IOutboxProcessorControl>() is not { } control)
            {
                return Results.NotFound("Outbox processor is not enabled.");
            }

            var timeout = timeoutSeconds is > 0
                ? TimeSpan.FromSeconds(timeoutSeconds.Value)
                : options.DefaultDrainTimeout;

            if (timeout > options.MaxDrainTimeout)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            }

            await control.DrainAsync(timeout, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new OutboxProcessorStateResponse { State = control.State });
        });
    }

    /// <summary>
    ///     Runs registered <see cref="IDiagnosticCheck" /> probes from <see cref="LiteBusHostManifest" />.
    /// </summary>
    /// <param name="manifest">The host manifest.</param>
    /// <param name="services">The request services.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="cancellationToken">A token that cancels probe execution.</param>
    /// <returns>A JSON payload describing probe outcomes.</returns>
    private static async Task<IResult> RunDiagnosticChecksAsync(
        LiteBusHostManifest manifest,
        IServiceProvider services,
        LiteBusManagementOptions options,
        CancellationToken cancellationToken)
    {
        var runResult = await DiagnosticCheckRunner.RunAsync(
                manifest,
                services,
                options.FailHealthWhenNoProbes,
                options.DiagnosticChecks,
                cancellationToken)
            .ConfigureAwait(false);

        List<DiagnosticProbeResponse> results = [
            ..runResult.Probes.Select(probe => new DiagnosticProbeResponse
            {
                Name = probe.Name,
                Status = probe.Status,
                Description = probe.Description,
                Data = probe.Data
            })
        ];

        return runResult.Status == DiagnosticAggregateStatus.Healthy
            ? Results.Ok(results)
            : Results.Json(results, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    ///     Executes an endpoint handler and maps unexpected exceptions to problem responses.
    /// </summary>
    /// <remarks>
    ///     Catches <see cref="Exception" /> at the ASP.NET management edge because handlers may surface
    ///     store, transport, or domain failures that must be logged and mapped to stable HTTP 500 responses.
    /// </remarks>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="handler">The endpoint handler.</param>
    /// <returns>The HTTP result.</returns>
    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<Task<IResult>> handler)
    {
        try
        {
            return await handler().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            LogManagementFailure(context, exception, InvalidManagementRequestMessage);
            return CreateManagementProblem(
                context,
                StatusCodes.Status400BadRequest,
                "Invalid management request",
                "The request does not satisfy the management endpoint contract.",
                "urn:litebus:management:invalid-request");
        }
#pragma warning disable CA1031 // ASP.NET management edge maps unhandled failures to HTTP 500 responses.
        catch (Exception exception)
        {
            LogManagementFailure(context, exception, ManagementOperationFailedMessage);
            return CreateManagementProblem(
                context,
                StatusCodes.Status500InternalServerError,
                "Management operation failed",
                "The server could not complete the management operation.",
                "urn:litebus:management:operation-failed");
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Executes a management endpoint handler and maps operator safety exceptions to bad requests.
    /// </summary>
    /// <remarks>
    ///     Catches <see cref="Exception" /> after domain-specific management exceptions because remaining
    ///     failures from inbox or outbox managers must be logged and mapped at this ASP.NET boundary.
    /// </remarks>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="handler">The endpoint handler.</param>
    /// <returns>The HTTP result.</returns>
    private static async Task<IResult> ExecuteManagementAsync(HttpContext context, Func<Task<IResult>> handler)
    {
        try
        {
            return await handler().ConfigureAwait(false);
        }
        catch (InboxManagementException exception)
        {
            LogManagementFailure(context, exception, ManagementSafetyRejectionMessage);
            return CreateSafetyRejectionProblem(context);
        }
        catch (OutboxManagementException exception)
        {
            LogManagementFailure(context, exception, ManagementSafetyRejectionMessage);
            return CreateSafetyRejectionProblem(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            LogManagementFailure(context, exception, InvalidManagementRequestMessage);
            return CreateManagementProblem(
                context,
                StatusCodes.Status400BadRequest,
                "Invalid management request",
                "The request does not satisfy the management endpoint contract.",
                "urn:litebus:management:invalid-request");
        }
#pragma warning disable CA1031 // Remaining inbox/outbox manager failures become HTTP 500 at this boundary.
        catch (Exception exception)
        {
            LogManagementFailure(context, exception, ManagementOperationFailedMessage);
            return CreateManagementProblem(
                context,
                StatusCodes.Status500InternalServerError,
                "Management operation failed",
                "The server could not complete the management operation.",
                "urn:litebus:management:operation-failed");
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Creates the stable problem response used when an operator safety rule rejects a request.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <returns>The operator safety problem response.</returns>
    private static IResult CreateSafetyRejectionProblem(HttpContext context)
    {
        return CreateManagementProblem(
            context,
            StatusCodes.Status400BadRequest,
            "Management request rejected",
            "The request was rejected by a management safety rule.",
            "urn:litebus:management:safety-rejection");
    }

    /// <summary>
    ///     Creates a problem response with a stable type and the request trace identifier.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="title">The stable problem title.</param>
    /// <param name="detail">The safe problem detail.</param>
    /// <param name="type">The stable problem type.</param>
    /// <returns>The problem response.</returns>
    private static IResult CreateManagementProblem(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string type)
    {
        return Results.Problem(
            detail: detail,
            instance: context.Request.Path,
            statusCode: statusCode,
            title: title,
            type: type,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = context.TraceIdentifier
            });
    }

    /// <summary>
    ///     Logs a management boundary failure with request context and a stable event identifier.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="exception">The failure raised by the endpoint handler.</param>
    /// <param name="logMessage">The precompiled structured log message.</param>
    private static void LogManagementFailure(
        HttpContext context,
        Exception exception,
        Action<ILogger, string, string, string, Exception?> logMessage)
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("LiteBus.Management");

        logMessage(
            logger,
            context.TraceIdentifier,
            context.Request.Method,
            context.Request.Path.Value ?? string.Empty,
            exception);
    }

    /// <summary>
    ///     Validates a selective requeue request before it reaches a store.
    /// </summary>
    /// <param name="request">The request containing message identifiers.</param>
    /// <param name="options">The management safety limits.</param>
    private static void ValidateBulkRequest(RequeueMessagesRequest? request, LiteBusManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBulkRequest(request.MessageIds, options);
    }

    /// <summary>
    ///     Validates a message identifier collection against the configured bulk limit.
    /// </summary>
    /// <param name="messageIds">The optional message identifiers.</param>
    /// <param name="options">The management safety limits.</param>
    private static void ValidateBulkRequest(IReadOnlyCollection<Guid>? messageIds, LiteBusManagementOptions options)
    {
        if (messageIds is not null && messageIds.Count > options.MaxBulkMessageIds)
        {
            throw new ArgumentOutOfRangeException(nameof(messageIds));
        }
    }

    /// <summary>
    ///     Validates and executes a selective inbox purge.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="filter">The purge filter.</param>
    /// <param name="confirm">Whether an unrestricted purge was confirmed.</param>
    /// <param name="messageIds">The optional message identifiers in the filter.</param>
    /// <param name="options">The management safety limits.</param>
    /// <param name="cancellationToken">A token that cancels the purge.</param>
    /// <returns>The number of deleted rows.</returns>
    private static async Task<int> PurgeInboxWithValidationAsync(
        IInboxManager manager,
        InboxMessageFilter filter,
        bool confirm,
        IReadOnlyCollection<Guid>? messageIds,
        LiteBusManagementOptions options,
        CancellationToken cancellationToken)
    {
        ValidateBulkRequest(messageIds, options);
        return await manager.PurgeAsync(filter, confirm, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Validates and executes a selective outbox purge.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="filter">The purge filter.</param>
    /// <param name="confirm">Whether an unrestricted purge was confirmed.</param>
    /// <param name="messageIds">The optional message identifiers in the filter.</param>
    /// <param name="options">The management safety limits.</param>
    /// <param name="cancellationToken">A token that cancels the purge.</param>
    /// <returns>The number of deleted rows.</returns>
    private static async Task<int> PurgeOutboxWithValidationAsync(
        IOutboxManager manager,
        OutboxMessageFilter filter,
        bool confirm,
        IReadOnlyCollection<Guid>? messageIds,
        LiteBusManagementOptions options,
        CancellationToken cancellationToken)
    {
        ValidateBulkRequest(messageIds, options);
        return await manager.PurgeAsync(filter, confirm, cancellationToken).ConfigureAwait(false);
    }

}
