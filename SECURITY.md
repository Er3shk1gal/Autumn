# Security Policy

## Supported versions

Irkalla.Kafka is pre-1.0. Security fixes are applied to the latest published version only.

| Version | Supported |
|---------|-----------|
| latest `0.x` | ✅ |
| older `0.x` | ❌ |

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately via GitHub's **[Security Advisories](https://github.com/Er3shk1gal/Irkalla/security/advisories/new)**
("Report a vulnerability"). Include:

- affected version(s),
- a description and, if possible, a minimal reproduction,
- the impact you foresee.

You can expect an acknowledgement within a few days. Once a fix is ready, a patched release is
published to NuGet and a GitHub Security Advisory is issued crediting the reporter (unless anonymity
is requested).

## What the project already does

Every change runs through automated security checks in CI before a release can publish:

- **Secret scanning** — gitleaks over the full history.
- **Dependency CVEs** — Trivy; a release is blocked if any dependency has CVSS ≥ 6.0.
- **SAST** — Semgrep (blocking) and CodeQL (scheduled + on PRs).
- **Dependency updates** — Dependabot.
- **Supply chain** — a CycloneDX SBOM is generated and signed (keyless, Sigstore/cosign) per release;
  packages are published via OIDC Trusted Publishing (no long-lived API keys in CI).

## Notes for operators

- TLS/SASL is handled by `librdkafka` (Confluent.Kafka); configure it via `Security` / `RawConfig`.
- Delivery is at-least-once — design handlers to be idempotent.
- DLQ messages carry an `error` header (exception message). The exception **stack trace** is
  **not** included by default (`IncludeStackTraceInDlq = false`); enable it only when the DLQ topic's
  access is trusted, and restrict read access to DLQ topics accordingly.
