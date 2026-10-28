# Contributing

You build, test, and document LiteBus from one solution file. This page gives you the commands and the rules your change must pass before review.

> **This page is for** developers who change LiteBus code, tests, samples, or docs.
>
> **After reading it** you can build the solution, run the tests, and document your change. See the [Roadmap](../roadmap/README.md) for planned work.

## Build

Build the whole solution from the repository root. The solution uses the `.slnx` format.

```bash
dotnet build LiteBus.slnx
```

`StyleCop.Analyzers` is referenced from `src/Directory.Build.props` and runs documentation rules (SA1600 through SA1629) as warnings via `src/.editorconfig`. Layout, naming, ordering, readability, and spacing rules stay off except one-type-per-file (SA1402), file-name-matches-type (SA1649), and file-scoped namespaces (IDE0161, error). Fix documentation warnings before you finish a change under `src/`. File header rules (SA1633 and related) stay off, so do not add copyright or license banners.

## Test

Run the full suite with this command.

```bash
dotnet test LiteBus.slnx
```

`LiteBus.Storage.IntegrationTests` uses Testcontainers and needs a running Docker daemon. Without Docker those tests skip during fixture setup with a message that says so. Exclude the PostgreSQL integration tests with this filter.

```bash
dotnet test LiteBus.slnx --filter "FullyQualifiedName!~PostgreSql"
```

See [Testing LiteBus](../testing/application-testing.md) for the in-memory test patterns and the registry helper used in host-based tests.

## XML Documentation

All C# under `src/` must carry XML documentation (`///`) on every construct, including `private` and `internal` members. The table lists the required tags per construct.

| Construct | Required tags |
| --- | --- |
| Type | `<summary>`; `<remarks>` when behavior is non-obvious |
| Member (any accessibility) | `<summary>`; `<param>` per parameter; `<returns>` when not void; `<typeparam>` per type parameter |
| Field (private and internal) | `<summary>` describing role and lifetime |
| Explicit interface implementation | `<inheritdoc />` or an explicit `<summary>` |
| Property | `<summary>`; `<value>` when the value's meaning is not obvious from the name |

Match the existing style: indent summary text with four spaces after the opening tag, use `<see cref="TypeName" />` for solution references, and use `<see langword="null" />`, `<see langword="true" />`, and `<see langword="false" />` where appropriate. Do not write documentation that only restates the identifier.

Public types and members in test helpers, shared fixtures, and sample entry points require XML documentation. Private test members and `benchmarks/` are exempt unless your change requires documentation there.

## Changelog

Update `Changelog.md` in the version's cumulative section as work lands when your change alters public API or documented behavior. Docs-only changes that alter no behavior need no changelog entry.

## Pull Request Expectations

Follow existing naming, project layout, and module patterns. Keep your change scoped to the task. Build the solution and fix documentation analyzer warnings before you submit. Run the test suite; when you cannot run Docker, CI runs the PostgreSQL integration tests.

## Next

- [Roadmap](../roadmap/README.md): planned work that is not part of the current contract
- [Testing LiteBus](../testing/application-testing.md): in-memory test patterns for applications
