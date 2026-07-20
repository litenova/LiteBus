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
