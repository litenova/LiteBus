# Contributing to LiteBus

Thank you for helping improve LiteBus. Start with the architecture guide and package
dependency graph before changing a public surface.

## Development workflow

1. Fork the repository and create a focused branch.
2. Make the smallest change that proves the behavior.
3. Add or update tests at the narrowest useful level.
4. Update XML documentation for every changed source member and update the relevant docs.
5. Run `dotnet build LiteBus.slnx --configuration Release` and
   `dotnet test LiteBus.slnx --configuration Release --no-build`.
6. Open a pull request with the behavior, compatibility impact, and verification commands.

Keep package roles and opt-in dependency boundaries intact. If a change needs a new
dependency edge or a breaking API, explain the trade-off in the pull request before
implementation.

## Pull requests

Use one theme per pull request. Include migration notes for persisted formats, transport
semantics, public APIs, or package references. Do not include credentials, broker dumps,
or customer data in issues, commits, or test fixtures.

Contributions that use generative AI tools must follow the review, disclosure, provenance,
and verification requirements in [`AI_POLICY.md`](AI_POLICY.md).

The repository's CI checks build, tests, documentation, package validation, benchmark
discovery, and skipped-test policy. A maintainer may request provider-specific integration
coverage before merging a transport or storage change.

## Releases

Pushing a `v*` tag runs the release workflow, which reruns every gate before publishing.

A major line is developed on its own branch named after it, such as `v7`, and merges to
`main` when it is ready to be the stable release. Pre-release tags may be cut from that
branch: a pre-release is only resolvable by a caller who asks for one, so it does not
change what an existing consumer gets. A stable tag must be reachable from `main`, and the
release workflow rejects one that is not, because a stable release is what NuGet resolves
without an opt-in and what the documentation site deploys from.

Notes come from the Changelog section for the version being released. Previews of one
version share that version's section rather than each carrying a fragment of it, so
`v7.0.0-preview.2` publishes the `## v7.0.0` notes as they stand at that point.
