# AI Use Policy

LiteBus is a maintainer-owned open-source library. AI tools may assist development, but they do not define the architecture, public API, behavior, or release decisions.

## Project History

The LiteBus Git history starts on [December 4, 2020](https://github.com/litenova/LiteBus/commit/f6d7ed2e532d6fa4c382b63038c057b8ff1da54d). The repository adopted the LiteBus name on [March 17, 2021](https://github.com/litenova/LiteBus/commit/74ca271c8586bed75f71e0055e5853d3a7a9cf91). Both dates precede the [GitHub Copilot technical preview](https://github.blog/news-insights/product-news/introducing-github-copilot-ai-pair-programmer/) from June 2021 and the [public release of ChatGPT](https://openai.com/index/chatgpt/) from November 2022.

The mediator implementation was already under active development before public generative coding assistants. Current development continues that codebase; generated output is not treated as project authority.

## Maintainer Responsibility

- A maintainer is accountable for every merged change.
- AI-generated output is treated as an untrusted draft. It must be read, understood, and checked against the repository before use.
- Public API, architecture, persistence, transport, and compatibility decisions require maintainer judgment.
- Tests, analyzers, documentation checks, and review requirements apply regardless of which tools assisted the author.
- A passing build does not excuse unclear ownership, copied material, unsupported claims, or code the contributor cannot explain.

## Acceptable Assistance

AI tools may help with repository navigation, draft code, repetitive edits, test cases, documentation review, and alternative designs. The contributor remains responsible for selecting the approach and verifying the result.

Material AI assistance must be disclosed in the pull request when generated code, prose, tests, or design content remains in the submitted diff. Routine completion, formatting, and search assistance do not need individual disclosure.

## Contribution Requirements

AI-assisted contributions must meet the same requirements as any other contribution:

- Follow `AGENTS.md`, architecture rules, API conventions, and package boundaries.
- Use repository source and current documentation as the authority for LiteBus behavior.
- Run the checks required for the changed area and report their results accurately.
- Verify generated citations, package names, APIs, examples, and behavioral claims against primary sources or executable code.
- Review generated code for correctness, security, cancellation, concurrency, allocation, and failure behavior where those concerns apply.
- Confirm that submitted material has acceptable provenance and does not reproduce code or prose under incompatible terms.
- Do not provide secrets, credentials, private consumer code, production data, or personal data to an AI service.

## Unacceptable Contributions

LiteBus does not accept:

- Bulk-generated code or prose that has not received line-by-line human review.
- Fabricated APIs, tests, benchmarks, citations, compatibility claims, or implementation status.
- Changes added only because an AI tool suggested a pattern or dependency.
- Generated abstractions that widen the public API without a concrete LiteBus use case.
- Documentation written as a chat transcript, development-session record, or unsupported marketing copy.
- Contributions whose author cannot explain the behavior, trade-offs, and verification.

## Consumer Expectations

Consumers should judge LiteBus by its source, tests, release artifacts, documented contracts, and maintainer review. AI assistance does not reduce those requirements and is never presented as evidence of quality.

Report policy concerns through the normal issue or security channels described in [SUPPORT.md](SUPPORT.md) and [SECURITY.md](SECURITY.md).
