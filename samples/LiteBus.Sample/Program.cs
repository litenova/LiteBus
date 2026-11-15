using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Sample;
using LiteBus.Sample.Auditing;
using LiteBus.Sample.CommandBatches;
using LiteBus.Sample.Commands;
using LiteBus.Sample.Events;
using LiteBus.Sample.ImmediateDispatch;
using LiteBus.Sample.Queries;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<PaymentLedger>();
builder.Services.AddSingleton<BatchPaymentLedger>();
builder.Services.AddSingleton<ImmediateOrderLedger>();
builder.Services.AddScoped<ITransactionalOutboxStore, SampleTransactionalOutboxStore>();
builder.Services.AddScoped<ITransactionalOutbox, StoreBoundTransactionalOutbox>();

builder.Services.AddLiteBus(liteBus =>
{
    var applicationAssembly = typeof(ProcessPaymentCommand).Assembly;

    // The trail and the outcome mapper are the shared half of auditing, so they are configured here;
    // each axis then decides for itself whether its messages produce records.
    liteBus.AddMessaging(messaging => messaging.UseAuditTrail<ConsoleAuditTrail>());

    // The batch and immediate order handlers stage their work in the sample unit of work, so one
    // mediation commits once and a batch commits once for all of its commands.
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(applicationAssembly).EnableAuditing().UseUnitOfWork<SampleUnitOfWork>());
    liteBus.AddQueries(queries => queries.RegisterFromAssembly(applicationAssembly).EnableAuditing());
    liteBus.AddEvents(events => events.RegisterFromAssembly(applicationAssembly));

    liteBus.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process");
        inbox.Contracts.Register<ApplyBatchPaymentCommand>("payments.apply-batch-payment");
        inbox.Contracts.Register<PlaceImmediateOrderCommand>("orders.place-immediate-order");
        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(options => options.PollInterval = TimeSpan.FromMilliseconds(100));
    });

    liteBus.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<PaymentProcessed>("payments.processed");
        outbox.Contracts.Register<OrderPlacedImmediately>("orders.placed");
        outbox.UseInMemoryStorage();
        outbox.UseInProcessDispatch();
        outbox.EnableImmediateDispatch();
        outbox.EnableOutboxProcessor(options => options.PollInterval = TimeSpan.FromMilliseconds(100));
    });
});

var app = builder.Build();

app.MapPost(
    "/payments",
    async (AcceptPaymentRequest request, IInbox inbox, CancellationToken cancellationToken) =>
    {
        var command = new ProcessPaymentCommand(request.PaymentId, request.Amount);
        var receipt = await inbox.AcceptAsync(command, cancellationToken).ConfigureAwait(false);

        return Results.Accepted($"/payments/{request.PaymentId}", new
        {
            MessageId = receipt.Id,
            receipt.Outcome
        });
    });

app.MapGet(
    "/payments/{paymentId:guid}",
    async (Guid paymentId, IQueryMediator queryMediator, CancellationToken cancellationToken) =>
    {
        var status = await queryMediator.QueryAsync(
                new GetPaymentStatusQuery(paymentId),
                cancellationToken)
            .ConfigureAwait(false);

        return status is null ? Results.NotFound() : Results.Ok(status);
    });

app.MapPost(
    "/batches/payments",
    async (AcceptBatchPaymentsRequest request, ICommandBatchMediator batchMediator, CancellationToken cancellationToken) =>
    {
        var commands = request.Payments
            .Select(static payment => (ICommand) new ApplyBatchPaymentCommand(payment.PaymentId, payment.Amount))
            .ToArray();

        var result = await batchMediator.TrySendManyAsync(commands, cancellationToken: cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            result.Outcome,
            result.FailedIndex,
            result.IsCommitted,
            Entries = result.Entries.Select(static entry => new
            {
                entry.Index,
                Command = entry.CommandType.Name,
                entry.Executed,
                Outcome = entry.Result?.Outcome.ToString()
            }).ToArray()
        });
    });

app.MapGet(
    "/batches/payments/{paymentId:guid}",
    (Guid paymentId, BatchPaymentLedger ledger) =>
    {
        var status = ledger.Find(paymentId);

        return status is null ? Results.NotFound() : Results.Ok(status);
    });

app.MapPost(
    "/immediate-orders",
    async (AcceptImmediateOrderRequest request, ICommandMediator commandMediator, CancellationToken cancellationToken) =>
    {
        var command = new PlaceImmediateOrderCommand(request.OrderId, request.Amount);
        await commandMediator.SendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);

        return Results.Accepted($"/immediate-orders/{request.OrderId}", new
        {
            OrderId = request.OrderId,
            Note = "The order committed and its event was dispatched immediately; the application log shows both."
        });
    });

app.MapGet(
    "/immediate-orders/{orderId:guid}",
    (Guid orderId, ImmediateOrderLedger ledger) =>
    {
        var status = ledger.Find(orderId);

        return status is null ? Results.NotFound() : Results.Ok(status);
    });

app.Run();
