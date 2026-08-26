using LiteBus.Commands;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Sample;
using LiteBus.Messaging.Abstractions;
using LiteBus.Sample.Auditing;
using LiteBus.Sample.Commands;
using LiteBus.Sample.Events;
using LiteBus.Sample.Queries;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<PaymentLedger>();
builder.Services.AddSingleton<IAuditTrail, ConsoleAuditTrail>();

builder.Services.AddLiteBus(liteBus =>
{
    var applicationAssembly = typeof(ProcessPaymentCommand).Assembly;

    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(applicationAssembly).EnableAuditing());
    liteBus.AddQueries(queries => queries.RegisterFromAssembly(applicationAssembly).EnableAuditing());
    liteBus.AddEvents(events => events.RegisterFromAssembly(applicationAssembly));

    liteBus.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process");
        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(options => options.PollInterval = TimeSpan.FromMilliseconds(100));
    });

    liteBus.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<PaymentProcessed>("payments.processed");
        outbox.UseInMemoryStorage();
        outbox.UseInProcessDispatch();
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

app.Run();
