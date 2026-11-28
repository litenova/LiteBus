using LiteBus.Auditing;
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

    liteBus.AddMessaging(_ => { });

    // Auditing is its own module: the sample writes records to its console trail, for commands and queries.
    liteBus.Modules.AddAuditing(auditing => auditing
        .ForCommands()
        .ForQueries()
        .UseTrail<ConsoleAuditTrail>());

    // The batch and immediate order handlers stage their work in the sample unit of work, so one
    // mediation commits once and a batch commits once for all of its commands.
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(applicationAssembly).UseUnitOfWork<SampleUnitOfWork>());
    liteBus.AddQueries(queries => queries.RegisterFromAssembly(applicationAssembly));
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

        var body = new
        {
            Outcome = result.Outcome.ToString(),
            result.FailedIndex,
            result.IsCommitted,
            Entries = result.Entries.Select(static entry => new
            {
                entry.Index,
                Command = entry.CommandType.Name,
                entry.Executed,
                Outcome = entry.Result?.Outcome.ToString(),
                entry.Result?.Reason,
                entry.Result?.Code
            }).ToArray()
        };

        // A batch a validator or guard refused rolled back as a whole; 422 tells the client nothing was applied.
        return result.IsCommitted ? Results.Ok(body) : Results.UnprocessableEntity(body);
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
