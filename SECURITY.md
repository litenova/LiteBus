# Security policy

## Supported Versions

| Version | Supported |
| --- | --- |
| 7.x | Yes |
| 6.x | Security fixes only, when the fix can be applied without changing the supported runtime or persisted contract |
| 5.x and older | No |

## Reporting a Vulnerability

Do not open a public issue, discussion, or pull request for an undisclosed vulnerability. Report it privately through either channel:

- Email `security@litebus.io`.
- Open a private report through [GitHub security advisories](https://github.com/litenova/LiteBus/security/advisories/new) for this repository.

Include the affected package and version, a minimal reproduction, the impact you observed or expect, and any proposed mitigation. Do not include secrets, production payloads, or personal data.

## What to Expect

- An acknowledgement within five working days.
- An assessment of severity and affected versions, using the rubric in [the threat model](.oss-scanner/threat_model.md#how-you-rate-severity).
- A fix in the supported lines, a published GitHub security advisory, and credit to the reporter unless they ask otherwise.

Coordinate public disclosure with the maintainers. The default disclosure window is 90 days from the report, or earlier once a fixed release is published.

## Scope

The scope covers the packages published from `src/`. `samples/`, `benchmarks/`, `tests/`, `scripts/`, and the documentation site in `site/` are out of scope. The [threat model](.oss-scanner/threat_model.md) lists what is security-critical, where untrusted input enters, and how severity is rated.
