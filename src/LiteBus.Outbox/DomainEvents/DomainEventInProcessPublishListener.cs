using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     Publishes the domain events one mediation recorded for in-process publication after its unit of work commits.
/// </summary>
/// <remarks>
///     <para>
///         Registered as a scoped <see cref="IUnitOfWorkCommitListener" /> when collected domain event staging is
///         enabled. The command and query unit-of-work completion handlers call it after
///         <see cref="IUnitOfWork.CommitAsync" /> returns, and a command batch calls it once after its single commit.
///     </para>
///     <para>
///         Each recorded event publishes as a root mediation with the cause recorded when it was staged. A mediation
///         that recorded nothing drains an empty buffer and never touches the event mediator.
///     </para>
/// </remarks>
internal sealed class DomainEventInProcessPublishListener : IUnitOfWorkCommitListener
{
    /// <summary>
    ///     The dispatch-scope provider used to resolve the event mediator lazily.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     The tracker holding the domain events recorded in this mediation scope.
    /// </summary>
    private readonly StagedInProcessDomainEvents _staged;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DomainEventInProcessPublishListener" /> class.
    /// </summary>
    /// <param name="staged">The tracker holding the domain events recorded in this mediation scope.</param>
    /// <param name="serviceProvider">The dispatch-scope provider used to resolve the event mediator.</param>
    public DomainEventInProcessPublishListener(StagedInProcessDomainEvents staged, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _staged = staged;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async Task OnCommittedAsync(CancellationToken cancellationToken = default)
    {
        var staged = _staged.Drain();

        if (staged.Count == 0)
        {
            return;
        }

        var mediator = _serviceProvider.GetService(typeof(IEventMediator)) as IEventMediator;

        if (mediator is null)
        {
            throw new InvalidOperationException(
                "Recorded domain events cannot publish in process because no IEventMediator is registered. " +
                "Call AddEvents before collecting domain events that publish in process.");
        }

        await DomainEventInProcessPublisher.PublishAsync(mediator, staged, cancellationToken).ConfigureAwait(false);
    }
}
