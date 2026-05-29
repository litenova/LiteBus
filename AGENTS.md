# LiteBus agent instructions

## XML documentation (required)

All C# under `src/` must use XML documentation comments (`///`) on every construct, including `private` and `internal` members. This applies to the shipping libraries consumers reference and to internal implementation details agents maintain.

### What must be documented

| Construct | Required tags |
|-----------|----------------|
| Namespace | Not required (no XML on namespace declarations) |
| Type (`class`, `struct`, `record`, `interface`, `enum`, `delegate`) | `<summary>`; `<remarks>` when behavior is non-obvious |
| Public / internal / protected members | `<summary>`; `<param>` per parameter; `<returns>` when not void; `<typeparam>` per type parameter |
| Private members | `<summary>` at minimum; `<param>` / `<returns>` / `<typeparam>` when applicable |
| Private and internal fields | `<summary>` describing role and lifetime |
| Explicit interface implementations | `<inheritdoc />` or explicit `<summary>` |
| Constructors | `<summary>`; `<param>` for each parameter |
| Properties | `<summary>`; `<value>` when the meaning of the value is not obvious from the name |

### Style (match existing LiteBus packages)

- Indent summary text with four spaces after the opening tag (same as current public API docs).
- Use `<see cref="TypeName" />` for references to types and members in this solution.
- Use `<see langword="null" />`, `<see langword="true" />`, and `<see langword="false" />` where appropriate.
- Prefer complete sentences in summaries.
- Do not document auto-generated designer or assembly attribute boilerplate.
- Replace member-level `//` comments with `///` when documenting that member; keep `//` only for local algorithm notes inside method bodies.

### Examples

```csharp
/// <summary>
///     Registers a message type with a stable contract name and version.
/// </summary>
/// <typeparam name="TMessage">The concrete message type to register.</typeparam>
/// <param name="name">The stable contract name stored in inbox and outbox envelopes.</param>
/// <param name="version">The positive contract version stored with the payload.</param>
/// <returns>The registry so module builders can chain registrations.</returns>
IMessageContractRegistry Register<TMessage>(string name, int version = 1)
    where TMessage : notnull;

/// <summary>
///     Gets the message registry used to register handlers and message types.
/// </summary>
private readonly IMessageRegistry _messageRegistry;
```

```csharp
/// <summary>
///     Links newly discovered handler descriptors to committed message descriptors.
/// </summary>
/// <param name="newDescriptors">The handler descriptors to link.</param>
private void LinkHandlersToCommittedMessages(IList<IHandlerDescriptor> newDescriptors)
```

### Out of scope

- `tests/`, `samples/`, and `benchmarks/` are not required to follow this rule unless a task explicitly says otherwise.
- Do not add XML comments that restate the identifier without adding meaning (for example, `/// <summary>Gets the count.</summary>` on `Count` is acceptable; `/// <summary>Count.</summary>` is not).
- Do not add file header blocks (`// <copyright>`, license banners). LiteBus uses per-member `///` documentation only.

### Verification

After editing `src/`:

```bash
dotnet build LiteBus.slnx
```

`StyleCop.Analyzers` is referenced from `src/Directory.Build.props`. Only **documentation** rule categories are warnings (`src/.editorconfig`); other StyleCop categories are disabled so layout and naming rules do not churn existing code. Fix documentation analyzer warnings (SA1600 through SA1629) before finishing a documentation task. File header rules (SA1633 and related) are disabled.

## General coding expectations

- Follow existing naming, project layout, and module patterns.
- Keep changes scoped to the task.
- Update `Changelog.md` under `v5.0.0` when public API or documented behavior changes.
