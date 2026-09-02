# Validation

A validator answers one question about a message: is it well-formed. LiteBus runs validators as their own pipeline stage, after guards and before shortcuts, and a failure is a return value rather than an exception. This page explains why validation is its own stage, what the caller receives when a message fails, and how to move a validator that used to throw.

It assumes the stage model from [The Handler Pipeline](handler-pipeline.md).

## Validation Is Not Authorization

The pipeline keeps three questions apart because the answers mean different things to whoever reads them later:

| Question | Contract | Outcome | Read by |
| --- | --- | --- | --- |
| May this happen? | `ICommandGuard<TCommand>` | `Denied` | A security review |
| Is this well-formed? | `ICommandValidator<TCommand>` | `Invalid` | The caller, so they can fix the message |
| Is this already done? | `ICommandShortcut<TCommand>` | `Answered` | Nobody; nothing was refused |

Recording a malformed field as a denial puts an entry in the list a security review reads that has nothing to do with permission. Recording a refusal as invalid input hides the entry that review is looking for. Two outcomes, kept apart, is the whole point.

The test for which contract to reach for: if the same message from a different caller would be treated differently, it is a guard. If the message would be wrong no matter who sent it, it is a validator.

## The Contract

```csharp
public sealed class TransferValidator : ICommandValidator<TransferCommand>
{
    public Task<Validity> ValidateAsync(
        TransferCommand command,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();

        if (command.Amount <= 0)
        {
            failures.Add(new ValidationFailure(
                "the amount must be positive",
                nameof(command.Amount),
                "AMOUNT_NOT_POSITIVE"));
        }

        if (string.IsNullOrWhiteSpace(command.Reference))
        {
            failures.Add(new ValidationFailure(
                "the reference must be supplied",
                nameof(command.Reference)));
        }

        return Task.FromResult(Validity.Invalid(failures));
    }
}
```

`Validity.Invalid` over an empty sequence is `Validity.Valid`, so the happy path needs no branch. For a single failure there is a shorter form:

```csharp
return Task.FromResult(Validity.Invalid("the amount must be positive", nameof(command.Amount)));
```

A `ValidationFailure` always carries a message written for a person. `Member` and `Code` are optional and LiteBus never interprets either; they exist so a caller, a refusal mapper, or a transport layer can act on the failure without parsing prose.

The axis contracts are `ICommandValidator<TCommand>`, `IQueryValidator<TQuery>`, and `IEventValidator<TEvent>`, each deriving from `IMessageValidator<TMessage>`.

## Every Validator Runs

This is the one decision stage that does not stop at the first answer. Every validator registered for the message runs, global then specific, and the stage gathers all their failures into one result.

That asymmetry is deliberate. A caller fixing a malformed message wants every problem at once rather than discovering them one round trip at a time. A caller who is not allowed to proceed needs one reason, and listing the rest would tell them more about the system than they should learn. So validators aggregate and guards stop.

```csharp
// Both validators run; the caller sees both failures.
builder.Register(typeof(TransferAmountValidator));
builder.Register(typeof(TransferReferenceValidator));
```

`[HandlerPriority]` still orders validators among themselves, which matters when one validator's message reads better before another's. It cannot move the stage.

## Why Validation Runs After Guards and Before Shortcuts

The order encodes what each stage may assume about its input, and the framework fixes it so no priority can change it.

Guards run first because an unauthorized caller should learn nothing from the response, including whether a field was malformed or a resource exists. Validators run before shortcuts because a malformed message must not claim an idempotency key or collect a cached answer; a shortcut keyed on a field validation would have rejected is a shortcut keyed on garbage. Pre-handlers run last because there is no point enriching a message that is about to be refused, rejected, or skipped.

Under a single pre-handler stage this ordering rested on priority numbers every author had to remember, and indirect handlers ran ahead of direct ones regardless of what those numbers said.

## What the Caller Receives

By default a validation failure reaches the caller as `LiteBusMessageInvalidException`, carrying every failure on `Failures`:

```csharp
try
{
    await commandMediator.SendAsync(command, cancellationToken);
}
catch (LiteBusMessageInvalidException invalid)
{
    return Results.ValidationProblem(invalid.Failures
        .GroupBy(failure => failure.Member ?? string.Empty)
        .ToDictionary(group => group.Key, group => group.Select(f => f.Message).ToArray()));
}
```

Like a denial, this is a decision rather than a fault. It does not reach error handlers, and `MediationExceptionFilters.IsRefusal` reports it as a decision so durable processors treat it as terminal rather than retrying a message that will fail identically every time.

An application that models failure as a value registers a refusal mapper instead, and the exception never happens:

```csharp
public sealed class ResultRefusalMapper : ICommandRefusalMapper<ICommand, Result>
{
    public Result Map(ICommand command, Refusal refusal) => refusal.Outcome switch
    {
        MediationOutcome.Denied  => Result.Forbidden(refusal.Code, refusal.Reason),
        MediationOutcome.Invalid => Result.Invalid(refusal.Reason),
        _                      => Result.Failure(refusal.Reason)
    };
}
```

One registration against `ICommand` covers every command producing that result type, so the shape of a failed result is defined once rather than in each validator. A mapper registered against a concrete message wins over it. See [The Handler Pipeline](handler-pipeline.md#refusal-mappers).

A message that produces no result, and any event, has nothing a mapper could return, so a failure there always raises.

## Moving a Validator That Threw

Before v7, `ICommandValidator<TCommand>.ValidateAsync` returned `Task` and reported a failure by throwing. That reported malformed input as a fault: error handlers saw it, the mediation reported `Failed`, and an audit trail could not tell it apart from a database timeout.

The change is a compile error rather than a silent behavior change, which is deliberate. Return the failures instead of raising:

```csharp
// Before
public Task ValidateAsync(TransferCommand command, CancellationToken cancellationToken = default)
{
    if (command.Amount <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(command));
    }

    return Task.CompletedTask;
}

// After
public Task<Validity> ValidateAsync(TransferCommand command, CancellationToken cancellationToken = default)
{
    return Task.FromResult(command.Amount <= 0
        ? Validity.Invalid("the amount must be positive", nameof(command.Amount))
        : Validity.Valid);
}
```

An adapter over an external validation library changes one method:

```csharp
public sealed class ExternalValidatorAdapter<TCommand> : ICommandValidator<TCommand>
    where TCommand : ICommand
{
    private readonly IExternalValidator<TCommand> _validator;

    public ExternalValidatorAdapter(IExternalValidator<TCommand> validator) => _validator = validator;

    public async Task<Validity> ValidateAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var result = await _validator.ValidateAsync(command, cancellationToken);

        return Validity.Invalid(result.Errors.Select(error =>
            new ValidationFailure(error.Message, error.PropertyName, error.Code)));
    }
}
```

A validator that genuinely needs to fault, because a lookup it depends on is unreachable, should still throw. That is a fault, not a verdict about the message, and the pipeline should report it as one.

### Landing the Build Before Converting Everything

A codebase with a hundred validators breaks in a hundred places at once, and a hundred-file commit cannot be reviewed. `ThrowingCommandValidator<TCommand, TException>` and `ThrowingQueryValidator<TQuery, TException>` let you land the build first:

```csharp
public sealed class TransferCommandValidator
    : ThrowingCommandValidator<TransferCommand, ValidationException>
{
    protected override Task ValidateOrThrowAsync(
        TransferCommand command,
        CancellationToken cancellationToken)
    {
        // The old body, unchanged.
        var errors = new ErrorCollection();

        if (command.Amount <= 0)
        {
            errors.Add(nameof(command.Amount), "the amount must be positive");
        }

        errors.ThrowIfInvalidCommand();
        return Task.CompletedTask;
    }

    protected override Validity Describe(ValidationException exception) =>
        Validity.Invalid(exception.Errors.Select(e => new ValidationFailure(e.Message, e.Member)));
}
```

Only the exception type you name is caught, so a genuine fault inside a validator still ends the mediation as a failure rather than being reported as a verdict.

Adapted and converted validators mix in one mediation, because the stage collects across both. A half-converted codebase behaves correctly rather than only after the last file lands.

The adapter is scaffolding. It cannot recover the behavior the change exists for: a throwing body stops at the first problem, so a message with three malformed fields still reports one failure. Give the adapter a deletion date, or the codebase keeps the validation model v7 replaced.

## Next

- [The Handler Pipeline](handler-pipeline.md) for the full stage order and the guard and shortcut contracts
- [Auditing](auditing.md) for how `Invalid` is recorded and why it stays out of the denial list
- [Handler Priority](handler-priority.md) for ordering validators among themselves
