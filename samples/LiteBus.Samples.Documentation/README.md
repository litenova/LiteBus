# LiteBus Documentation Snippets

This ASP.NET Core project holds the compiled sources behind the code blocks in the guides. Each snippet lives in a marked `docs-snippet` region, and each guide links its block to that region with a snippet-source comment, so the documented code is verified by the compiler.

The project is a compile target, not a runnable sample. `Program.cs` shows the complete ASP.NET Core with EF Core outbox registration with the interceptor call only; an application adds its database provider call in the same options lambdas.

Build the project from the repository root:

```bash
dotnet build samples/LiteBus.Samples.Documentation
```

Regions:

- `Program.cs#efcore-outbox-program` - the complete registration in the ASP.NET Core with EF Core outbox guide.
- `Orders/OrdersDbContext.cs#efcore-outbox-db-context` - the documented context.
- `Orders/OrderUnitOfWork.cs#efcore-outbox-unit-of-work` - the documented unit of work.
- `OutboxEnqueueSnippet.cs#efcore-outbox-enqueue` - the transactional enqueue call.
- `Orders/OrderProjectionHandler.cs#efcore-outbox-immediate-handler` - the documented immediate handler.
- `HealthCheckRegistrationSnippet.cs#health-check-registration` - the health-check registration in the diagnostics guide.
- `Pipeline/RejectSelfApproval.cs#pipeline-command-guard` - the guard in the handler pipeline guide.
- `Pipeline/WrapInEnvelope.cs#pipeline-query-post-handler` - the post-handler in the handler pipeline guide.
- `Auditing/CancelOrderCommandAuditDefinition.cs#audit-definition-with-target` - the audit definition with a target selector in the handler pipeline guide.
