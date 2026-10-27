using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Hands the idempotency key the HTTP endpoint carried to the idempotency shortcut.
/// </summary>
/// <remarks>
///     <para>
///         The idempotency shortcut reads a context-supplied key from the execution-context data store, which only a
///         pipeline stage can write: the store belongs to one mediation and does not exist until the mediation starts.
///         A caller outside the pipeline can only pass mediation settings, which land in the string-keyed items
///         collection. This guard bridges the two: it runs in the guard stage, before the shortcut stage, reads the
///         entry <see cref="CommandMediationSettingsExtensions" /> wrote, and stores it as a
///         <see cref="SuppliedIdempotencyKey" /> the shortcut resolves.
///     </para>
///     <para>
///         Register it once on the command module alongside the filter, and it applies to every command indirectly:
///     </para>
///     <code><![CDATA[
/// commands.Register<IdempotencyKeyGuard>();
/// ]]></code>
///     <para>
///         It always allows. A mediation that carries no key passes through untouched, so commands sent without the
///         endpoint filter behave exactly as before. It also leaves a key another stage already supplied alone, so a
///         command-specific guard that derives its own key keeps precedence over the header.
///     </para>
/// </remarks>
public sealed class IdempotencyKeyGuard : ICommandGuard<ICommand>
{
    /// <summary>
    ///     The execution context of the mediation in flight.
    /// </summary>
    private readonly IExecutionContext _executionContext;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyKeyGuard" /> class.
    /// </summary>
    /// <param name="executionContext">The execution context of the mediation in flight.</param>
    public IdempotencyKeyGuard(IExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        _executionContext = executionContext;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_executionContext.Data.Contains<SuppliedIdempotencyKey>() &&
            _executionContext.Items.TryGetValue(IdempotencyKeyItemKeys.MediationItem, out var value) &&
            value is SuppliedIdempotencyKey supplied)
        {
            _executionContext.Data.Set(supplied);
        }

        return Task.FromResult(Verdict.Allow);
    }
}
