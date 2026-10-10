# Contributing

This page explains how to build, test, and document LiteBus when contributing.

## Build

The solution uses the `.slnx` format. Build the whole solution from the repository root:

```bash
dotnet build LiteBus.slnx
```

`StyleCop.Analyzers` is referenced from `src/Directory.Build.props`. Only documentation rule categories (SA1600 through SA1629) are warnings; layout and naming rules are disabled so existing code does not churn. Fix documentation analyzer warnings before finishing a change under `src/`. File header rules (SA1633 and related) are disabled, so do not add copyright or license banners.

## Test

Run all tests:

```bash
dotnet test LiteBus.slnx
```

`LiteBus.Storage.PostgreSql.IntegrationTests` uses Testcontainers and needs a running Docker daemon. Without Docker those tests are skipped with a message saying so. To run the unit tests only:

```bash
dotnet test LiteBus.slnx --filter "FullyQualifiedName!~PostgreSql"
```

See [Testing LiteBus](../testing/application-testing.md) for the in-memory test patterns and the registry-clearing helper used in host-based tests.

## XML Documentation

All C# under `src/` must carry XML documentation (`///`) on every construct, including `private` and `internal` members. This covers types, members, constructors, properties, and private and internal fields. The required tags per construct:

| Construct | Required tags |
| --- | --- |
| Type | `<summary>`; `<remarks>` when behavior is non-obvious |
| Member (any accessibility) | `<summary>`; `<param>` per parameter; `<returns>` when not void; `<typeparam>` per type parameter |
| Field (private and internal) | `<summary>` describing role and lifetime |
| Explicit interface implementation | `<inheritdoc />` or an explicit `<summary>` |
| Property | `<summary>`; `<value>` when the value's meaning is not obvious from the name |

Match the existing style: indent summary text with four spaces after the opening tag, use `<see cref="TypeName" />` for solution references, and use `<see langword="null" />`, `<see langword="true" />`, and `<see langword="false" />` where appropriate. Do not add documentation that only restates the identifier.

Public types and members in test helpers, shared fixtures, and sample entry points require XML documentation. Private test members and `benchmarks/` are exempt unless the change requires documentation there.

## Changelog

Update `Changelog.md` under `## Unreleased` (or the current release section after a tag) when public API or documented behavior changes. Docs-only changes that do not alter behavior do not need a changelog entry.

## Pull Request Expectations

- Follow existing naming, project layout, and module patterns.
- Keep changes scoped to the task.
- Build the solution and fix documentation analyzer warnings before submitting.
- Run the test suite; if you cannot run Docker, say so in the pull request and rely on CI for the PostgreSQL integration tests.

## Next

To see where the project is headed and which packages are planned, read the [Roadmap](../roadmap/README.md).
