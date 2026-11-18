using LiteBus.Commands;
using LiteBus.Events;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Extensions.Diagnostics.HealthChecks;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Samples.Documentation.Orders;
using Microsoft.EntityFrameworkCore;

// <docs-snippet efcore-outbox-program>
var builder = WebApplication.CreateBuilder(args);

// Required: scoped context for the transactional path. ITransactionalOutbox<OrdersDbContext>
// resolves this context from the current scope. Factory-only registration cannot satisfy it.
// Configure the database provider (such as options.UseNpgsql(connectionString)) in this
// lambda alongside the interceptor.
builder.Services.AddDbContext<OrdersDbContext>((serviceProvider, options) =>
    options.AddLiteBusOutboxInterceptor(
        serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>()));

// Required: factory for the singleton store. EfCoreOutboxStore creates and disposes one
// context per operation through IDbContextFactory<OrdersDbContext>. Configure the same
// provider and interceptor here as on the scoped registration above.
builder.Services.AddDbContextFactory<OrdersDbContext>((serviceProvider, options) =>
    options.AddLiteBusOutboxInterceptor(
        serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>()));

// Required: messaging, commands with a unit of work, events, and the outbox.
builder.Services.AddLiteBus(liteBus =>
{
    // Required: message contracts, serialization, and the mediation core.
    // The outbox module declares a dependency on messaging and does not register it.
    liteBus.AddMessaging(_ =>
    {
    });

    liteBus.AddCommands(commands =>
    {
        // Required: discovers command handlers in the application assembly.
        commands.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly);

        // Required: commits the staged unit of work after every successful mediation.
        // EnableImmediateDispatch fails composition without a registered IUnitOfWork.
        commands.UseUnitOfWork<OrderUnitOfWork>();
    });

    liteBus.AddEvents(events =>
    {
        // Required: discovers the event handlers in-process dispatch publishes to.
        events.RegisterFromAssembly(typeof(OrderSubmitted).Assembly);
    });

    liteBus.AddOutbox(outbox =>
    {
        // Required: one stable contract name and version per stored event type.
        outbox.Contracts.Register<OrderSubmitted>("orders.events.submitted", 1);

        outbox.UseEntityFrameworkCoreStorage(ef =>
        {
            // Required: binds the store and ITransactionalOutbox<TContext> to OrdersDbContext.
            ef.UseDbContext<OrdersDbContext>();

            // Required: registers LiteBusOutboxSaveChangesInterceptor in DI so the
            // AddDbContext and AddDbContextFactory calls above can attach it.
            ef.EnableSaveChangesInterceptor();

            // Optional but recommended: fails module build when EnableSaveChangesInterceptor
            // was not called, instead of failing at the first SaveChanges.
            ef.EnforceTransactionalSetup();
        });

        // Required: exactly one dispatcher. The processor has nothing to dispatch through
        // without it, and module build fails when the processor is enabled without one.
        outbox.UseInProcessDispatch();

        // Optional: one envelope per subscribed handler, each with its own retry and replay.
        // Required when a handler class carries [DispatchImmediately]: without per-handler
        // delivery, only an event type carrying the attribute is immediate.
        outbox.EnablePerHandlerDelivery();

        // Optional: dispatches staged immediate envelopes right after the commit, with the
        // processor as the fallback. Requires the unit of work, the dispatcher, and a store
        // implementing IOutboxTargetedLeaseStore; FallbackDelay must exceed Timeout.
        outbox.EnableImmediateDispatch(dispatch =>
        {
            dispatch.Timeout = TimeSpan.FromSeconds(10);
            dispatch.FallbackDelay = TimeSpan.FromSeconds(30);
        });

        // Required for background publication: polls due envelopes and dispatches them.
        // Without it, only immediate dispatch (when enabled) publishes anything.
        outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));

        // Optional: deletes terminal rows on a schedule. Without it, published rows
        // accumulate. Retention must be set, or the loop runs and deletes nothing.
        outbox.EnableCleanup(cleanup =>
        {
            cleanup.Interval = TimeSpan.FromHours(1);
            cleanup.Retention = TimeSpan.FromDays(30);
        });
    });
});

// Optional: exposes manifest diagnostic probes as an ASP.NET Core health check.
// Without it, probes still run through GET /litebus/health when mapped below.
builder.Services.AddHealthChecks().AddLiteBus(options =>
{
    options.FailHealthWhenNoProbes = true;
    options.DiagnosticChecks = new DiagnosticCheckRunOptions
    {
        Timeout = TimeSpan.FromSeconds(3),
        MaxParallelism = 4,
        RunTimeout = TimeSpan.FromSeconds(10)
    };
});

// Optional: registers management endpoint options. Without it, the routes below
// cannot be mapped and operators have no HTTP access to rows or processor state.
builder.Services.AddLiteBusManagement(options =>
{
    options.AuthorizationPolicy = "LiteBusOperator";
});

var app = builder.Build();

// Optional host wiring: exposes the health check registered above.
app.MapHealthChecks("/health");

// Optional: maps inbox and outbox operator routes plus GET /litebus/health.
// Requires authentication unless AllowAnonymousManagement is set for local demos.
app.AddLiteBusManagementEndpoints();

app.Run();
// </docs-snippet>
