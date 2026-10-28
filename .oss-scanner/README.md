# OSS Scanner Enrolment

This folder holds what Anthropic's [OSS Scanner](https://red.anthropic.com/oss-scanner/) needs to scan LiteBus: the build environment, the threat model, and the enrolment file that is submitted to the scanner's repository.

| File | Purpose |
| --- | --- |
| `Dockerfile` | It builds the solution and runs the unit tests with network access, so the scanner's offline agent can audit and exercise the code. It is referenced by `dockerfile` in `project.yaml`. |
| `threat_model.md` | It describes where untrusted input enters, which components matter most, how to exercise them, and the severity rubric. It is referenced by `threat_model` in `project.yaml`. |
| `project.yaml` | This is the enrolment file. A copy is submitted as `projects/litebus/project.yaml`. |

## Security-Critical Scope

LiteBus runs inside applications and processes untrusted input: messages from brokers that other services can publish to, rows written by other tenants and services, and HTTP requests to its management and idempotency endpoints. It resolves contract names from those messages to CLR types and deserializes payloads, composes SQL for its PostgreSQL stores, enforces tenant isolation and idempotency for financial and other side-effecting commands, and encrypts payloads at rest. A defect can therefore lead to arbitrary type instantiation, SQL injection, cross-tenant data access, or a payment or other command executed twice.

## Applying

1. Merge this folder to `main` with the `SECURITY.md` contact in place (`security@litebus.io`), because the scanner clones `main` and verifies the security contact.
2. Build the image locally from the repository root and confirm the unit tests pass inside it: `docker build -f .oss-scanner/Dockerfile .`
3. Fork `https://github.com/anthropics/oss-scanner`, copy `project.yaml` to `projects/litebus/project.yaml`, and run the repository's `tools/validate.py` against it.
4. Open the pull request from a GitHub account of a core maintainer. Include the paragraph under "Security-Critical Scope" in its description.

To pause reports later, set `disabled: true` in the submitted file. To leave, delete `projects/litebus/` in a pull request.
