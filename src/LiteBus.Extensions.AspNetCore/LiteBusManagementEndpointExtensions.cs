using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LiteBus.Inbox.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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

        options ??= new LiteBusManagementOptions();
        var prefix = options.RoutePrefix.Trim('/');

        var inboxGroup = endpoints.MapGroup($"/{prefix}/inbox");
        var outboxGroup = endpoints.MapGroup($"/{prefix}/outbox");

        if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicy))
        {
            inboxGroup = inboxGroup.RequireAuthorization(options.AuthorizationPolicy);
            outboxGroup = outboxGroup.RequireAuthorization(options.AuthorizationPolicy);
        }

        inboxGroup.MapGet("/messages", QueryInboxMessagesAsync);
        inboxGroup.MapPost("/messages/requeue-dead-letters", RequeueInboxDeadLettersAsync);
        inboxGroup.MapDelete("/messages", PurgeInboxMessagesAsync);
        inboxGroup.MapGet("/status-counts", GetInboxStatusCountsAsync);

        outboxGroup.MapGet("/messages", QueryOutboxMessagesAsync);
        outboxGroup.MapPost("/messages/requeue-dead-letters", RequeueOutboxDeadLettersAsync);
        outboxGroup.MapDelete("/messages", PurgeOutboxMessagesAsync);
        outboxGroup.MapGet("/status-counts", GetOutboxStatusCountsAsync);

        endpoints.MapGet($"/{prefix}/health", RunDiagnosticChecksAsync);

        return endpoints;
    }

    /// <summary>
    ///     Queries inbox messages using the supplied filter and page request from the query string.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="filter">The message filter.</param>
    /// <param name="pageRequest">The page request.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching inbox message page.</returns>
    private static Task<IResult> QueryInboxMessagesAsync(
        IInboxManager manager,
        [AsParameters] InboxMessageFilter filter,
        [AsParameters] InboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await manager.QueryAsync(filter, pageRequest, cancellationToken).ConfigureAwait(false)));

    /// <summary>
    ///     Requeues all dead-lettered inbox messages.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    private static Task<IResult> RequeueInboxDeadLettersAsync(
        IInboxManager manager,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await manager.RequeueDeadLettersAsync(cancellationToken).ConfigureAwait(false)));

    /// <summary>
    ///     Purges inbox messages that match the supplied filter.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="filter">The message filter.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    private static Task<IResult> PurgeInboxMessagesAsync(
        IInboxManager manager,
        [AsParameters] InboxMessageFilter filter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await manager.PurgeAsync(filter, cancellationToken).ConfigureAwait(false)));

    /// <summary>
    ///     Returns inbox status counts.
    /// </summary>
    /// <param name="manager">The inbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>Status counts grouped by <see cref="InboxStatus" />.</returns>
    private static Task<IResult> GetInboxStatusCountsAsync(
        IInboxManager manager,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await manager.GetStatusCountsAsync(cancellationToken).ConfigureAwait(false)));

    /// <summary>
    ///     Queries outbox messages using the supplied filter and page request from the query string.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="filter">The message filter.</param>
    /// <param name="pageRequest">The page request.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The matching outbox message page.</returns>
    private static Task<IResult> QueryOutboxMessagesAsync(
        IOutboxManager manager,
        [AsParameters] OutboxMessageFilter filter,
        [AsParameters] OutboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await manager.QueryAsync(filter, pageRequest, cancellationToken).ConfigureAwait(false)));

    /// <summary>
    ///     Requeues all dead-lettered outbox messages.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the replay operation.</param>
    /// <returns>The number of messages requeued.</returns>
    private static Task<IResult> RequeueOutboxDeadLettersAsync(
        IOutboxManager manager,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await manager.RequeueDeadLettersAsync(cancellationToken).ConfigureAwait(false)));

    /// <summary>
    ///     Purges outbox messages that match the supplied filter.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="filter">The message filter.</param>
    /// <param name="cancellationToken">A token that cancels the purge operation.</param>
    /// <returns>The number of deleted rows.</returns>
    private static Task<IResult> PurgeOutboxMessagesAsync(
        IOutboxManager manager,
        [AsParameters] OutboxMessageFilter filter,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await manager.PurgeAsync(filter, cancellationToken).ConfigureAwait(false)));

    /// <summary>
    ///     Returns outbox status counts.
    /// </summary>
    /// <param name="manager">The outbox manager.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>Status counts grouped by <see cref="OutboxStatus" />.</returns>
    private static Task<IResult> GetOutboxStatusCountsAsync(
        IOutboxManager manager,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await manager.GetStatusCountsAsync(cancellationToken).ConfigureAwait(false)));

    /// <summary>
    ///     Runs registered <see cref="IDiagnosticCheck" /> probes from <see cref="LiteBusHostManifest" />.
    /// </summary>
    /// <param name="manifest">The host manifest.</param>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">A token that cancels probe execution.</param>
    /// <returns>A JSON payload describing probe outcomes.</returns>
    private static async Task<IResult> RunDiagnosticChecksAsync(
        LiteBusHostManifest manifest,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var results = new List<DiagnosticProbeResponse>();

        foreach (var descriptor in manifest.DiagnosticChecks)
        {
            var check = (IDiagnosticCheck)services.GetRequiredService(descriptor.ImplementationType);
            var result = await check.CheckAsync(cancellationToken).ConfigureAwait(false);
            results.Add(new DiagnosticProbeResponse(descriptor.Name, result.Status, result.Description));
        }

        var healthy = results.Count == 0 || results.All(item => item.Status == DiagnosticStatus.Healthy);
        return healthy ? Results.Ok(results) : Results.Json(results, statusCode: StatusCodes.Status503ServiceUnavailable);
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
    ///     JSON payload for a single diagnostic probe outcome.
    /// </summary>
    /// <param name="Name">The probe name.</param>
    /// <param name="Status">The reported status.</param>
    /// <param name="Description">The probe summary text.</param>
    private sealed record DiagnosticProbeResponse(string Name, DiagnosticStatus Status, string Description);
}
