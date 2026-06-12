using LiteBus.Inbox.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ProcessorState = LiteBus.Inbox.Abstractions.ProcessorState;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Maps LiteBus operator management and diagnostic endpoints on ASP.NET Core hosts.
/// </summary>
public static class LiteBusManagementEndpointExtensions
{
    /// <summary>
    ///     Maps inbox and outbox management endpoints backed by <see cref="IInboxManager" /> and
    ///     <see cref="IOutboxManager" />.
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
        var prefix = options.RoutePrefix.Trim('/');

        var inboxGroup = ApplyManagementAuthorization(endpoints.MapGroup(BuildManagementRoute(prefix, "inbox")), options);
        var outboxGroup = ApplyManagementAuthorization(endpoints.MapGroup(BuildManagementRoute(prefix, "outbox")), options);

        inboxGroup.MapGet("/messages", QueryInboxMessagesAsync);
        inboxGroup.MapGet("/messages/{messageId:guid}", GetInboxMessageAsync);
        inboxGroup.MapPost("/messages/requeue", RequeueInboxMessagesAsync);
        inboxGroup.MapPost("/messages/requeue-dead-letters", RequeueInboxDeadLettersAsync);
        inboxGroup.MapDelete("/messages", PurgeInboxMessagesAsync);
        inboxGroup.MapGet("/status-counts", GetInboxStatusCountsAsync);
        inboxGroup.MapGet("/schema", GetInboxSchemaAsync);
        inboxGroup.MapGet("/retention/status", GetInboxRetentionStatusAsync);
        inboxGroup.MapPost("/retention/purge", RunInboxRetentionPurgeAsync);
        inboxGroup.MapGet("/processor/state", GetInboxProcessorStateAsync);
        inboxGroup.MapPost("/processor/pause", PauseInboxProcessorAsync);
        inboxGroup.MapPost("/processor/resume", ResumeInboxProcessorAsync);
        inboxGroup.MapPost("/processor/drain", DrainInboxProcessorAsync);

        outboxGroup.MapGet("/messages", QueryOutboxMessagesAsync);
        outboxGroup.MapGet("/messages/{messageId:guid}", GetOutboxMessageAsync);
        outboxGroup.MapPost("/messages/requeue", RequeueOutboxMessagesAsync);
        outboxGroup.MapPost("/messages/requeue-dead-letters", RequeueOutboxDeadLettersAsync);
        outboxGroup.MapDelete("/messages", PurgeOutboxMessagesAsync);
        outboxGroup.MapGet("/status-counts", GetOutboxStatusCountsAsync);
        outboxGroup.MapGet("/schema", GetOutboxSchemaAsync);
        outboxGroup.MapGet("/retention/status", GetOutboxRetentionStatusAsync);
        outboxGroup.MapPost("/retention/purge", RunOutboxRetentionPurgeAsync);
        outboxGroup.MapGet("/processor/state", GetOutboxProcessorStateAsync);
        outboxGroup.MapPost("/processor/pause", PauseOutboxProcessorAsync);
        outboxGroup.MapPost("/processor/resume", ResumeOutboxProcessorAsync);
        outboxGroup.MapPost("/processor/drain", DrainOutboxProcessorAsync);

        ApplyManagementAuthorization(
            endpoints.MapGet(
                BuildManagementRoute(prefix, "health"),
                (LiteBusHostManifest manifest, IServiceProvider services, CancellationToken cancellationToken) =>
                    RunDiagnosticChecksAsync(manifest, services, options, cancellationToken)),
            options);

        return endpoints;
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
    /// <param name="manager">The inbox manager.</param>
    /// <param name="parameters">The bound query string parameters.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching inbox message page.</returns>
    private static Task<IResult> QueryInboxMessagesAsync(
        IInboxManager manager,
        [AsParameters] InboxMessageQueryBinding parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(
            await manager.QueryAsync(parameters.ToFilter(), parameters.ToPageRequest(), cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns one inbox message by identifier.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching envelope or a not-found response.</returns>
    private static Task<IResult> GetInboxMessageAsync(
        IInboxManager manager,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            var message = await manager.GetMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
            return message is null ? Results.NotFound() : Results.Ok(message);
        });
    }

    /// <summary>
    ///     Requeues selected inbox messages by identifier.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="request">The message identifiers to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    private static Task<IResult> RequeueInboxMessagesAsync(
        IInboxManager manager,
        [FromBody] RequeueMessagesRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.RequeueAsync(request.MessageIds, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Requeues all dead-lettered inbox messages.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    private static Task<IResult> RequeueInboxDeadLettersAsync(
        IInboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.RequeueDeadLettersAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Purges inbox messages that match the supplied filter.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="parameters">The bound query string parameters.</param>
    /// <param name="confirmRequest">The JSON body that confirms unrestricted purge.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    private static Task<IResult> PurgeInboxMessagesAsync(
        IInboxManager manager,
        [AsParameters] InboxMessagePurgeBinding parameters,
        [FromBody] PurgeConfirmRequest? confirmRequest,
        CancellationToken cancellationToken)
    {
        return ExecuteManagementAsync(async () => Results.Ok(
            await manager.PurgeAsync(
                    parameters.ToFilter(),
                    confirmRequest?.Confirm ?? false,
                    cancellationToken)
                .ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns inbox status counts.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>Status counts grouped by <see cref="InboxStatus" />.</returns>
    private static Task<IResult> GetInboxStatusCountsAsync(
        IInboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.GetStatusCountsAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns inbox schema version metadata.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>Expected and recorded schema versions.</returns>
    private static Task<IResult> GetInboxSchemaAsync(
        IInboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.GetSchemaInfoAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns inbox retention cleanup status.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>The configured retention policy and most recent cleanup outcome.</returns>
    private static Task<IResult> GetInboxRetentionStatusAsync(
        IInboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.GetRetentionStatusAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Triggers an immediate inbox retention purge.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of rows deleted.</returns>
    private static Task<IResult> RunInboxRetentionPurgeAsync(
        IInboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.RunRetentionPurgeAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns the inbox processor loop state.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <returns>The current processor state or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> GetInboxProcessorStateAsync(IServiceProvider services)
    {
        return ExecuteAsync(() =>
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
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the gate.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> PauseInboxProcessorAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
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
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> ResumeInboxProcessorAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
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
    /// <param name="services">The request services.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="timeoutSeconds">The optional drain timeout override in seconds.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for drain completion.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> DrainInboxProcessorAsync(
        IServiceProvider services,
        LiteBusManagementOptions options,
        [FromQuery] int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            if (services.GetService<IInboxProcessorControl>() is not { } control)
            {
                return Results.NotFound("Inbox processor is not enabled.");
            }

            var timeout = timeoutSeconds is > 0
                ? TimeSpan.FromSeconds(timeoutSeconds.Value)
                : options.DefaultDrainTimeout;

            await control.DrainAsync(timeout, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new ProcessorStateResponse { State = control.State });
        });
    }

    /// <summary>
    ///     Queries outbox messages using the supplied filter and page request from the query string.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="parameters">The bound query string parameters.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching outbox message page.</returns>
    private static Task<IResult> QueryOutboxMessagesAsync(
        IOutboxManager manager,
        [AsParameters] OutboxMessageQueryBinding parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(
            await manager.QueryAsync(parameters.ToFilter(), parameters.ToPageRequest(), cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns one outbox message by identifier.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching envelope or a not-found response.</returns>
    private static Task<IResult> GetOutboxMessageAsync(
        IOutboxManager manager,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            var message = await manager.GetMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
            return message is null ? Results.NotFound() : Results.Ok(message);
        });
    }

    /// <summary>
    ///     Requeues selected outbox messages by identifier.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="request">The message identifiers to requeue.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    private static Task<IResult> RequeueOutboxMessagesAsync(
        IOutboxManager manager,
        [FromBody] RequeueMessagesRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.RequeueAsync(request.MessageIds, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Requeues all dead-lettered outbox messages.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    private static Task<IResult> RequeueOutboxDeadLettersAsync(
        IOutboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.RequeueDeadLettersAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Purges outbox messages that match the supplied filter.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="parameters">The bound query string parameters.</param>
    /// <param name="confirmRequest">The JSON body that confirms unrestricted purge.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    private static Task<IResult> PurgeOutboxMessagesAsync(
        IOutboxManager manager,
        [AsParameters] OutboxMessagePurgeBinding parameters,
        [FromBody] PurgeConfirmRequest? confirmRequest,
        CancellationToken cancellationToken)
    {
        return ExecuteManagementAsync(async () => Results.Ok(
            await manager.PurgeAsync(
                    parameters.ToFilter(),
                    confirmRequest?.Confirm ?? false,
                    cancellationToken)
                .ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns outbox status counts.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>Status counts grouped by <see cref="OutboxStatus" />.</returns>
    private static Task<IResult> GetOutboxStatusCountsAsync(
        IOutboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.GetStatusCountsAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns outbox schema version metadata.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>Expected and recorded schema versions.</returns>
    private static Task<IResult> GetOutboxSchemaAsync(
        IOutboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.GetSchemaInfoAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns outbox retention cleanup status.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>The configured retention policy and most recent cleanup outcome.</returns>
    private static Task<IResult> GetOutboxRetentionStatusAsync(
        IOutboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.GetRetentionStatusAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Triggers an immediate outbox retention purge.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of rows deleted.</returns>
    private static Task<IResult> RunOutboxRetentionPurgeAsync(
        IOutboxManager manager,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () => Results.Ok(await manager.RunRetentionPurgeAsync(cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    ///     Returns the outbox processor loop state.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <returns>The current processor state or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> GetOutboxProcessorStateAsync(IServiceProvider services)
    {
        return ExecuteAsync(() =>
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
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for the gate.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> PauseOutboxProcessorAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
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
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">A token reserved for future cancellation support.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> ResumeOutboxProcessorAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
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
    /// <param name="services">The request services.</param>
    /// <param name="options">The management endpoint options.</param>
    /// <param name="timeoutSeconds">The optional drain timeout override in seconds.</param>
    /// <param name="cancellationToken">A token used to cancel waiting for drain completion.</param>
    /// <returns>A confirmation payload or a not-found response when the processor is disabled.</returns>
    private static Task<IResult> DrainOutboxProcessorAsync(
        IServiceProvider services,
        LiteBusManagementOptions options,
        [FromQuery] int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            if (services.GetService<IOutboxProcessorControl>() is not { } control)
            {
                return Results.NotFound("Outbox processor is not enabled.");
            }

            var timeout = timeoutSeconds is > 0
                ? TimeSpan.FromSeconds(timeoutSeconds.Value)
                : options.DefaultDrainTimeout;

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
        if (manifest.DiagnosticChecks.Count == 0)
        {
            if (options.FailHealthWhenNoProbes)
            {
                var degraded = new[]
                {
                    new DiagnosticProbeResponse
                    {
                        Name = "litebus.probes",
                        Status = DiagnosticStatus.Degraded,
                        Description = "No diagnostic probes are registered.",
                        Data = null
                    }
                };

                return Results.Json(degraded, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(Array.Empty<DiagnosticProbeResponse>());
        }

        var results = new List<DiagnosticProbeResponse>();

        foreach (var descriptor in manifest.DiagnosticChecks)
        {
            var check = (IDiagnosticCheck) services.GetRequiredService(descriptor.ImplementationType);
            var result = await check.CheckAsync(cancellationToken).ConfigureAwait(false);
            results.Add(new DiagnosticProbeResponse
            {
                Name = descriptor.Name,
                Status = result.Status,
                Description = result.Description,
                Data = result.Data
            });
        }

        var healthy = results.All(item => item.Status == DiagnosticStatus.Healthy);

        return healthy
            ? Results.Ok(results)
            : Results.Json(results, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    ///     Executes an endpoint handler and maps unexpected exceptions to problem responses.
    /// </summary>
    /// <param name="handler">The endpoint handler.</param>
    /// <returns>The HTTP result.</returns>
    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> handler)
    {
        try
        {
            return await handler().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    ///     Executes a management endpoint handler and maps operator safety exceptions to bad requests.
    /// </summary>
    /// <param name="handler">The endpoint handler.</param>
    /// <returns>The HTTP result.</returns>
    private static async Task<IResult> ExecuteManagementAsync(Func<Task<IResult>> handler)
    {
        try
        {
            return await handler().ConfigureAwait(false);
        }
        catch (InboxManagementException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (OutboxManagementException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (Exception exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    ///     JSON payload that confirms an unrestricted message purge.
    /// </summary>
    private sealed record PurgeConfirmRequest
    {
        /// <summary>
        ///     Gets or sets a value indicating whether the caller confirms deleting all rows matched by the filter.
        /// </summary>
        /// <value>
        ///     Must be <see langword="true" /> when the query string does not narrow the purge filter.
        /// </value>
        public bool Confirm { get; set; }
    }

    /// <summary>
    ///     JSON payload for a selective requeue request.
    /// </summary>
    private sealed record RequeueMessagesRequest
    {
        /// <summary>
        ///     Gets the message identifiers to requeue when the request body omits the array.
        /// </summary>
        public IReadOnlyList<Guid> MessageIds { get; } = Array.Empty<Guid>();
    }

    /// <summary>
    ///     JSON payload for inbox processor state responses.
    /// </summary>
    private sealed record ProcessorStateResponse
    {
        /// <summary>
        ///     Gets the reported inbox processor state.
        /// </summary>
        public ProcessorState State { get; init; }
    }

    /// <summary>
    ///     JSON payload for outbox processor state responses.
    /// </summary>
    private sealed record OutboxProcessorStateResponse
    {
        /// <summary>
        ///     Gets the reported outbox processor state.
        /// </summary>
        public Outbox.Abstractions.ProcessorState State { get; init; }
    }

    /// <summary>
    ///     JSON payload for a single diagnostic probe outcome.
    /// </summary>
    private sealed record DiagnosticProbeResponse
    {
        /// <summary>
        ///     Gets the probe name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        ///     Gets the reported status.
        /// </summary>
        public DiagnosticStatus Status { get; init; }

        /// <summary>
        ///     Gets the probe summary text.
        /// </summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>
        ///     Gets optional structured values from the probe.
        /// </summary>
        public IReadOnlyDictionary<string, object>? Data { get; init; }
    }
}