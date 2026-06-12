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

## Cursor Cloud specific instructions

The VM has .NET SDKs 8, 9, and 10 installed (tests multi-target `net8.0;net9.0;net10.0`) and Docker.

### Building and testing need public signing

`src/LiteBus.Runtime` and `src/LiteBus.PostgreSql` declare `InternalsVisibleTo` with a fixed strong-name public key, so every project must be signed with that key to compile. The private key is the `STRONG_NAME_KEY` CI secret and is not available here. The workaround is OSS public signing against a public-key-only `LiteBus.snk` (git-ignored), which does not need the private key.

The startup update script writes `LiteBus.snk` if it is missing, and `~/.bashrc` exports `SignAssembly=true`, `PublicSign=true`, and `AssemblyOriginatorKeyFile=/workspace/LiteBus.snk`. With those in place, the documented commands work unchanged:

```bash
dotnet build LiteBus.slnx   # also the StyleCop/XML-doc lint gate
dotnet test LiteBus.slnx
```

If a shell does not pick up the exports (for example a stripped environment), pass them explicitly: `dotnet build LiteBus.slnx -p:SignAssembly=true -p:PublicSign=true -p:AssemblyOriginatorKeyFile=$PWD/LiteBus.snk`. Do not commit `LiteBus.snk`; committing it would flip on signing for contributors who lack the private key and break their builds.

### PostgreSQL integration tests need a running Docker daemon

`tests/LiteBus.PostgreSql.IntegrationTests` uses `Testcontainers.PostgreSql`, which starts a real PostgreSQL container. Docker is not managed by systemd here, so start the daemon manually if `docker info` fails:

```bash
sudo dockerd &
```

The first run pulls the `postgres` image. Unit-test projects have no Docker dependency.

### Running the sample application

`samples/LiteBus.Samples.NetCore` is an ASP.NET Core Web API exposing the `Orders` endpoints (`POST /api/Orders`, `GET /api/Orders/{id}`) backed by the command, query, and event modules. Run it in the Development environment for the Swagger UI at `/swagger`, and bind to HTTP to avoid the dev HTTPS certificate:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 \
  dotnet run --project samples/LiteBus.Samples.NetCore
```

The `Failed to determine the https port for redirect` warning is expected with an HTTP-only binding and is harmless. The sample stores nothing: `GET` returns a generated `OrderDto`, while `POST` runs the full pipeline and writes `[PlaceOrderCommandHandler]` and `[OrderPlacedEventHandler]` lines to the console.
