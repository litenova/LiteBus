using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Abstractions.Tenancy;

namespace LiteBus.Mediator.UnitTests.Authorization.Policies;

/// <summary>
///     Records the authorization evaluations a mediation performs, in observation order.
/// </summary>
internal sealed class PolicyEvaluationRecorder
{
    /// <summary>
    ///     Gets the evaluation steps observed so far, in observation order.
    /// </summary>
    public List<string> Steps { get; } = [];
}

/// <summary>
///     The permissions the fake lookup grants, shared with the composition under test.
/// </summary>
internal sealed class PolicyTestOptions
{
    /// <summary>
    ///     Gets the permissions the fake lookup grants.
    /// </summary>
    public HashSet<string> Granted { get; } = new(StringComparer.Ordinal);
}

/// <summary>
///     Grants the permissions <see cref="PolicyTestOptions" /> holds.
/// </summary>
internal sealed class FakePermissionLookup : IPermissionLookup
{
    /// <summary>
    ///     The permissions this lookup grants.
    /// </summary>
    private readonly PolicyTestOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakePermissionLookup" /> class.
    /// </summary>
    /// <param name="options">The permissions this lookup grants.</param>
    public FakePermissionLookup(PolicyTestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(
        ExecutionIdentity identity,
        string permission,
        AuthorizationContext context,
        CancellationToken cancellationToken)
    {
        return new ValueTask<bool>(_options.Granted.Contains(permission));
    }
}

/// <summary>
///     A condition that records its evaluation as the first step and allows.
/// </summary>
internal sealed class FirstPolicyCondition : IAuthorizationCondition
{
    /// <summary>
    ///     Records the evaluations the guard performs.
    /// </summary>
    private readonly PolicyEvaluationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FirstPolicyCondition" /> class.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    public FirstPolicyCondition(PolicyEvaluationRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        _recorder.Steps.Add("first-condition");

        return new ValueTask<PolicyResult>(PolicyResult.Allow);
    }
}

/// <summary>
///     A condition that records its evaluation as the second step and allows.
/// </summary>
internal sealed class SecondPolicyCondition : IAuthorizationCondition
{
    /// <summary>
    ///     Records the evaluations the guard performs.
    /// </summary>
    private readonly PolicyEvaluationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SecondPolicyCondition" /> class.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    public SecondPolicyCondition(PolicyEvaluationRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        _recorder.Steps.Add("second-condition");

        return new ValueTask<PolicyResult>(PolicyResult.Allow);
    }
}

/// <summary>
///     A condition that records its evaluation and denies.
/// </summary>
internal sealed class DenyPolicyCondition : IAuthorizationCondition
{
    /// <summary>
    ///     Records the evaluations the guard performs.
    /// </summary>
    private readonly PolicyEvaluationRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DenyPolicyCondition" /> class.
    /// </summary>
    /// <param name="recorder">Records the evaluations the guard performs.</param>
    public DenyPolicyCondition(PolicyEvaluationRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        _recorder = recorder;
    }

    /// <inheritdoc />
    public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        _recorder.Steps.Add("deny-condition");

        return new ValueTask<PolicyResult>(PolicyResult.Deny(
            "test.condition-deny",
            "the deny condition refused the message."));
    }
}

/// <summary>
///     A policy that records its evaluation and answers a fixed result.
/// </summary>
/// <param name="Recorder">Records the evaluations the guard performs.</param>
/// <param name="Name">The step recorded when this policy evaluates.</param>
/// <param name="Result">The result this policy answers.</param>
internal sealed record RecordingPolicy(PolicyEvaluationRecorder Recorder, string Name, PolicyResult Result) : MessagePolicy
{
    /// <inheritdoc />
    public override ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        Recorder.Steps.Add(Name);

        return new ValueTask<PolicyResult>(Result);
    }
}

/// <summary>
///     Resolves a fixed sign-in window while recording how often and for which tenants it was read.
/// </summary>
internal sealed class CountingSignInWindow : IRecentSignInWindow
{
    /// <summary>
    ///     The window this source answers.
    /// </summary>
    private readonly TimeSpan _window;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CountingSignInWindow" /> class.
    /// </summary>
    /// <param name="window">The window this source answers.</param>
    public CountingSignInWindow(TimeSpan window)
    {
        _window = window;
    }

    /// <summary>
    ///     Gets how often the window was resolved.
    /// </summary>
    public int CallCount { get; private set; }

    /// <summary>
    ///     Gets the tenant of each evaluation that resolved the window, in observation order.
    /// </summary>
    public List<string?> ObservedTenants { get; } = [];

    /// <inheritdoc />
    public ValueTask<TimeSpan> GetWindowAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        CallCount++;
        ObservedTenants.Add(context.Identity.TenantId);

        return new ValueTask<TimeSpan>(_window);
    }
}

/// <summary>
///     Resolves a per-tenant sign-in window, falling back when the tenant carries no entry.
/// </summary>
internal sealed class TenantSignInWindow : IRecentSignInWindow
{
    /// <summary>
    ///     The window per tenant, compared ordinally.
    /// </summary>
    private readonly Dictionary<string, TimeSpan> _windows;

    /// <summary>
    ///     The window answered when the tenant carries no entry.
    /// </summary>
    private readonly TimeSpan _fallback;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TenantSignInWindow" /> class.
    /// </summary>
    /// <param name="windows">The window per tenant, compared ordinally.</param>
    /// <param name="fallback">The window answered when the tenant carries no entry.</param>
    public TenantSignInWindow(Dictionary<string, TimeSpan> windows, TimeSpan fallback)
    {
        ArgumentNullException.ThrowIfNull(windows);

        _windows = windows;
        _fallback = fallback;
    }

    /// <summary>
    ///     Gets how often the window was resolved.
    /// </summary>
    public int CallCount { get; private set; }

    /// <inheritdoc />
    public ValueTask<TimeSpan> GetWindowAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        CallCount++;

        var tenant = context.Identity.TenantId;
        var window = tenant is not null && _windows.TryGetValue(tenant, out var configured)
            ? configured
            : _fallback;

        return new ValueTask<TimeSpan>(window);
    }
}

/// <summary>
///     Answers a fixed authorization decision while recording the requests it observed.
/// </summary>
internal sealed class RecordingDecisionPoint : IAuthorizationDecisionPoint
{
    /// <summary>
    ///     The decision this decision point answers.
    /// </summary>
    private readonly PolicyResult _result;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingDecisionPoint" /> class.
    /// </summary>
    /// <param name="result">The decision this decision point answers.</param>
    public RecordingDecisionPoint(PolicyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _result = result;
    }

    /// <summary>
    ///     Gets the decision requests observed so far, in observation order.
    /// </summary>
    public List<AuthorizationDecisionRequest> Requests { get; } = [];

    /// <inheritdoc />
    public ValueTask<PolicyResult> DecideAsync(AuthorizationDecisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Requests.Add(request);

        return new ValueTask<PolicyResult>(_result);
    }
}

/// <summary>
///     A decision point no composition registers, used to assert the missing-registration fault.
/// </summary>
internal sealed class UnregisteredDecisionPoint : IAuthorizationDecisionPoint
{
    /// <inheritdoc />
    public ValueTask<PolicyResult> DecideAsync(AuthorizationDecisionRequest request, CancellationToken cancellationToken)
    {
        return new ValueTask<PolicyResult>(PolicyResult.Allow);
    }
}

/// <summary>
///     A sign-in window no composition registers, used to assert the missing-registration fault.
/// </summary>
internal sealed class UnregisteredSignInWindow : IRecentSignInWindow
{
    /// <inheritdoc />
    public ValueTask<TimeSpan> GetWindowAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        return new ValueTask<TimeSpan>(TimeSpan.FromMinutes(5));
    }
}

/// <summary>
///     Answers a fixed tenant verdict while recording the tenant identifiers it observed.
/// </summary>
internal sealed class FixedTenantValidator : ITenantIdentifierValidator
{
    /// <summary>
    ///     Whether this validator accepts the tenant identifiers it observes.
    /// </summary>
    private readonly bool _valid;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FixedTenantValidator" /> class.
    /// </summary>
    /// <param name="valid">Whether this validator accepts the tenant identifiers it observes.</param>
    public FixedTenantValidator(bool valid)
    {
        _valid = valid;
    }

    /// <summary>
    ///     Gets the tenant identifiers observed so far, in observation order.
    /// </summary>
    public List<string> ObservedTenants { get; } = [];

    /// <inheritdoc />
    public ValueTask<bool> IsValidAsync(
        string tenantId,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ObservedTenants.Add(tenantId);

        return new ValueTask<bool>(_valid);
    }
}

/// <summary>
///     The loaded invoice a resource condition shares with its handler.
/// </summary>
/// <param name="Id">The invoice identifier.</param>
internal sealed record InvoiceRecord(string Id);

/// <summary>
///     Loads invoice records while counting how often each invoice was loaded.
/// </summary>
internal sealed class InvoiceStore
{
    /// <summary>
    ///     Gets how often each invoice was loaded, by invoice identifier.
    /// </summary>
    public Dictionary<string, int> LoadsByInvoice { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Loads the invoice with the given identifier.
    /// </summary>
    /// <param name="invoiceId">The invoice identifier to load.</param>
    /// <returns>The loaded invoice.</returns>
    public InvoiceRecord Load(string invoiceId)
    {
        LoadsByInvoice[invoiceId] = LoadsByInvoice.TryGetValue(invoiceId, out var loads) ? loads + 1 : 1;

        return new InvoiceRecord(invoiceId);
    }
}

/// <summary>
///     Loads the invoice the payment names through the dispatch scope and shares it with the handler.
/// </summary>
internal sealed class InvoiceAccessCondition : IAuthorizationCondition
{
    /// <inheritdoc />
    public ValueTask<PolicyResult> EvaluateAsync(AuthorizationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var payment = (CapturePolicyPaymentCommand) context.Message;
        var store = (InvoiceStore) context.Services.GetService(typeof(InvoiceStore))!;
        var invoice = store.Load(payment.InvoiceId);

        if (context.ExecutionContext is { } execution)
        {
            execution.Data.Set(invoice);
        }

        return new ValueTask<PolicyResult>(PolicyResult.Allow);
    }
}

/// <summary>
///     A message carrying the subject an ownership policy reads.
/// </summary>
/// <param name="SubjectId">The owning subject identifier, or <see langword="null" /> when the message names none.</param>
internal sealed record PolicySubjectMessage(string? SubjectId);

/// <summary>
///     A message of another type, used to assert an ownership policy refuses what it does not apply to.
/// </summary>
internal sealed record PolicyOtherMessage;

/// <summary>
///     A command whose authorization position requires the vaults.open policy.
/// </summary>
[Authorized("vaults.open")]
internal sealed class OpenPolicyVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="OpenPolicyVaultCommand" />.
/// </summary>
internal sealed class OpenPolicyVaultCommandHandler : ICommandHandler<OpenPolicyVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(OpenPolicyVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose authorization position requires the vaults.seal policy.
/// </summary>
[Authorized("vaults.seal")]
internal sealed class SealPolicyVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="SealPolicyVaultCommand" />.
/// </summary>
internal sealed class SealPolicyVaultCommandHandler : ICommandHandler<SealPolicyVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(SealPolicyVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose authorization position names a policy no composition registers.
/// </summary>
[Authorized("vaults.unmapped")]
internal sealed class UnknownPolicyVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="UnknownPolicyVaultCommand" />.
/// </summary>
internal sealed class UnknownPolicyVaultCommandHandler : ICommandHandler<UnknownPolicyVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(UnknownPolicyVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command that records why it needs no authorization rather than omitting a position.
/// </summary>
[AuthorizationExempt("the vault catalogue is public, so there is no actor to authorize")]
internal sealed class BrowsePolicyVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="BrowsePolicyVaultCommand" />.
/// </summary>
internal sealed class BrowsePolicyVaultCommandHandler : ICommandHandler<BrowsePolicyVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(BrowsePolicyVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command declaring no authorization position, used to assert the undeclared decision.
/// </summary>
internal sealed class DraftPolicyVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="DraftPolicyVaultCommand" />.
/// </summary>
internal sealed class DraftPolicyVaultCommandHandler : ICommandHandler<DraftPolicyVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(DraftPolicyVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command naming the account it closes, used to assert an ownership policy.
/// </summary>
[Authorized("vaults.close-account")]
internal sealed class ClosePolicyAccountCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the account the command closes.
    /// </summary>
    public string? OwnerId { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="ClosePolicyAccountCommand" />.
/// </summary>
internal sealed class ClosePolicyAccountCommandHandler : ICommandHandler<ClosePolicyAccountCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(ClosePolicyAccountCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose policy loads the invoice it captures through a resource condition.
/// </summary>
[Authorized("payments.capture")]
internal sealed class CapturePolicyPaymentCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the invoice the command captures.
    /// </summary>
    public string InvoiceId { get; set; } = "inv-1";

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }

    /// <summary>
    ///     Gets or sets the invoice the handler observed in the execution-context data.
    /// </summary>
    public InvoiceRecord? ObservedInvoice { get; set; }
}

/// <summary>
///     Handles <see cref="CapturePolicyPaymentCommand" /> by reading the invoice its condition loaded.
/// </summary>
internal sealed class CapturePolicyPaymentCommandHandler : ICommandHandler<CapturePolicyPaymentCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(CapturePolicyPaymentCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;

        var data = AmbientExecutionContext.GetCurrentOrDefault()?.Data;
        message.ObservedInvoice = data is not null && data.TryGet<InvoiceRecord>(out var invoice) ? invoice : null;

        return Task.CompletedTask;
    }
}
