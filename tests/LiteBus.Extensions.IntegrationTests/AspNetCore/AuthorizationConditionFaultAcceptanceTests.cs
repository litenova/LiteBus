using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Extensions.IntegrationTests.AspNetCore;

/// <summary>
///     HTTP acceptance tests proving that an authorization condition reporting a fault answers 500 with the fault
///     code, while a condition denying answers 403 with the refusal code.
/// </summary>
public sealed class AuthorizationConditionFaultAcceptanceTests
{
    /// <summary>
    ///     The policy name the vault command declares.
    /// </summary>
    private const string VaultPolicyName = "vaults.open";

    /// <summary>
    ///     The reason the missing-port condition reports on its fault.
    /// </summary>
    private const string MissingLookupReason = "No permission lookup is registered, so the condition cannot decide.";

    /// <summary>
    ///     The reason the denying condition reports on its denial.
    /// </summary>
    private const string ConditionDenyReason = "The condition refused the message.";

    /// <summary>
    ///     The problem detail property name.
    /// </summary>
    private const string DetailProperty = "detail";

    /// <summary>
    ///     The JSON options shared by the test endpoints.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     Verifies that a policy-scoped condition reporting a fault answers 500 with the fault code.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Post_WhenPolicyScopedConditionReportsFault_AnswersInternalServerErrorWithFaultCode()
    {
        using var host = await CreateHostAsync(policies => policies
            .Add(VaultPolicyName, MessagePolicy.Condition<MissingPortCondition>())
            .RegisterCondition<MissingPortCondition>()).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.PostAsync("/vaults/open", content: null).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        problem.GetProperty("status").GetInt32().Should().Be(500);
        problem.GetProperty("title").GetString().Should().Be("Internal server error");
        problem.GetProperty("type").GetString().Should().Be("urn:litebus:authorization:permission-lookup-missing");
        problem.GetProperty("code").GetString().Should().Be(AuthorizationPolicyRefusalCodes.PermissionLookupMissing);
        problem.GetProperty(DetailProperty).GetString().Should().Be(MissingLookupReason);

        host.Services.GetRequiredService<AuthorizationHandlerRecorder>().Handled.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a policy-scoped condition denying answers 403 with the refusal code.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Post_WhenPolicyScopedConditionDenies_AnswersForbiddenWithRefusalCode()
    {
        using var host = await CreateHostAsync(policies => policies
            .Add(VaultPolicyName, MessagePolicy.Condition<DenyingCondition>())
            .RegisterCondition<DenyingCondition>()).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.PostAsync("/vaults/open", content: null).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        problem.GetProperty("status").GetInt32().Should().Be(403);
        problem.GetProperty("title").GetString().Should().Be("Request denied");
        problem.GetProperty("type").GetString().Should().Be("urn:litebus:authorization:forbidden");
        problem.GetProperty("code").GetString().Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
        problem.GetProperty(DetailProperty).GetString().Should().Be(ConditionDenyReason);

        host.Services.GetRequiredService<AuthorizationHandlerRecorder>().Handled.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a contributed condition reporting a fault answers 500 with the fault code.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Post_WhenContributedConditionReportsFault_AnswersInternalServerErrorWithFaultCode()
    {
        using var host = await CreateHostAsync(policies => policies
            .Add(VaultPolicyName, MessagePolicy.Allow())
            .AddCondition<MissingPortCondition>()).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.PostAsync("/vaults/open", content: null).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        problem.GetProperty("status").GetInt32().Should().Be(500);
        problem.GetProperty("title").GetString().Should().Be("Internal server error");
        problem.GetProperty("type").GetString().Should().Be("urn:litebus:authorization:permission-lookup-missing");
        problem.GetProperty("code").GetString().Should().Be(AuthorizationPolicyRefusalCodes.PermissionLookupMissing);
        problem.GetProperty(DetailProperty).GetString().Should().Be(MissingLookupReason);

        host.Services.GetRequiredService<AuthorizationHandlerRecorder>().Handled.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a contributed condition denying answers 403 with the refusal code.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Post_WhenContributedConditionDenies_AnswersForbiddenWithRefusalCode()
    {
        using var host = await CreateHostAsync(policies => policies
            .Add(VaultPolicyName, MessagePolicy.Allow())
            .AddCondition<DenyingCondition>()).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.PostAsync("/vaults/open", content: null).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        problem.GetProperty("status").GetInt32().Should().Be(403);
        problem.GetProperty("title").GetString().Should().Be("Request denied");
        problem.GetProperty("type").GetString().Should().Be("urn:litebus:authorization:forbidden");
        problem.GetProperty("code").GetString().Should().Be(AuthorizationPolicyRefusalCodes.Forbidden);
        problem.GetProperty(DetailProperty).GetString().Should().Be(ConditionDenyReason);

        host.Services.GetRequiredService<AuthorizationHandlerRecorder>().Handled.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that an allowed policy answers 200 and runs the handler.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task Post_WhenConditionAllows_AnswersOkAndRunsHandler()
    {
        using var host = await CreateHostAsync(policies => policies
            .Add(VaultPolicyName, MessagePolicy.Allow())).ConfigureAwait(false);
        using var client = host.GetTestClient();

        var response = await client.PostAsync("/vaults/open", content: null).ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        body.GetProperty("opened").GetBoolean().Should().BeTrue();

        host.Services.GetRequiredService<AuthorizationHandlerRecorder>().Handled.Should().BeTrue();
    }

    /// <summary>
    ///     Builds and starts a test host with authorization policies, the vault command, and the vault endpoint.
    /// </summary>
    /// <param name="configurePolicies">Collects the policies and conditions under test.</param>
    /// <returns>The started host.</returns>
    private static Task<IHost> CreateHostAsync(Action<MessagePolicyBuilder> configurePolicies)
    {
        ArgumentNullException.ThrowIfNull(configurePolicies);

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddProblemDetails();
                    services.AddLiteBusExceptionHandler();
                    services.AddSingleton<AuthorizationHandlerRecorder>();

                    services.AddLiteBus(registry =>
                    {
                        registry.AddMessaging(messaging => messaging.AddAuthorizationPolicies(configurePolicies));

                        registry.AddCommands(commands =>
                        {
                            commands.Register<OpenVaultCommand>();
                            commands.Register<OpenVaultCommandHandler>();
                        });
                    });
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseExceptionHandler();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/vaults/open", OpenVaultAsync);
                    });
                });
            });

        return builder.StartAsync();
    }

    /// <summary>
    ///     Sends the vault command and writes the receipt as the POST response.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the response is written.</returns>
    private static async Task OpenVaultAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var mediator = context.RequestServices.GetRequiredService<ICommandMediator>();
        await mediator.SendAsync(new OpenVaultCommand(), cancellationToken: context.RequestAborted).ConfigureAwait(false);

        context.Response.StatusCode = (int) HttpStatusCode.OK;
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new OpenVaultResponse { Opened = true },
            JsonOptions,
            context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    ///     Records whether the vault handler ran for one acceptance test run.
    /// </summary>
    public sealed class AuthorizationHandlerRecorder
    {
        /// <summary>
        ///     Gets or sets a value indicating whether the vault handler ran.
        /// </summary>
        public bool Handled { get; set; }
    }

    /// <summary>
    ///     A command opening one vault through the POST endpoint.
    /// </summary>
    [Authorized(VaultPolicyName)]
    public sealed class OpenVaultCommand : ICommand
    {
    }

    /// <summary>
    ///     Opens the vault and records that the handler ran.
    /// </summary>
    public sealed class OpenVaultCommandHandler : ICommandHandler<OpenVaultCommand>
    {
        /// <summary>
        ///     Records whether the handler ran.
        /// </summary>
        private readonly AuthorizationHandlerRecorder _recorder;

        /// <summary>
        ///     Initializes a new instance of the <see cref="OpenVaultCommandHandler" /> class.
        /// </summary>
        /// <param name="recorder">Records whether the handler ran.</param>
        public OpenVaultCommandHandler(AuthorizationHandlerRecorder recorder)
        {
            ArgumentNullException.ThrowIfNull(recorder);

            _recorder = recorder;
        }

        /// <summary>
        ///     Handles the command.
        /// </summary>
        /// <param name="message">The command.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>A task that completes when the command is handled.</returns>
        public Task HandleAsync(OpenVaultCommand message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            _recorder.Handled = true;

            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A condition that reports a fault when no permission lookup is registered, instead of denying.
    /// </summary>
    public sealed class MissingPortCondition : IAuthorizationCondition
    {
        /// <inheritdoc />
        public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Services.GetService(typeof(IPermissionLookup)) is null)
            {
                return new ValueTask<PolicyResult>(PolicyResult.Fault(
                    AuthorizationPolicyRefusalCodes.PermissionLookupMissing,
                    MissingLookupReason));
            }

            return new ValueTask<PolicyResult>(PolicyResult.Allow);
        }
    }

    /// <summary>
    ///     A condition that denies with the stable refusal code.
    /// </summary>
    public sealed class DenyingCondition : IAuthorizationCondition
    {
        /// <inheritdoc />
        public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);

            return new ValueTask<PolicyResult>(PolicyResult.Deny(
                AuthorizationPolicyRefusalCodes.Forbidden,
                ConditionDenyReason));
        }
    }

    /// <summary>
    ///     The response the POST endpoint writes when the vault opens.
    /// </summary>
    public sealed record OpenVaultResponse
    {
        /// <summary>
        ///     Gets a value indicating whether the vault opened.
        /// </summary>
        public required bool Opened { get; init; }
    }
}
