# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| `main` (latest) | Yes |
| Older releases | Security patches backported where feasible |

## Reporting a Vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**

Report via [GitHub private vulnerability reporting](https://github.com/adpmca/diva-ai/security/advisories/new) or email `security@[your-domain]`.

**SLA:**
- Acknowledge within 72 hours
- Triage and severity assessment within 7 days
- Patch timeline communicated after triage

## Deployment Security Notes

### `Credentials__MasterKey`
AES-256-GCM key encrypting all MCP credential secrets stored in the database. Must be set via environment variable in production — never committed to source control. If left empty, an ephemeral key is generated per startup (dev only); credentials encrypted with an ephemeral key are lost on restart.

Generate a stable key: `openssl rand -base64 32`

### `LocalAuth__SigningKey`
HMAC-SHA256 key for local JWT signing. Must be at least 32 characters. Changing this key invalidates all active sessions.

### API Keys (`diva_` prefix)
Platform API keys are SHA-256 hashed before storage — the raw key is shown only once at creation time. Treat them as passwords.

### Changing `AppBranding__Slug` after go-live
Changes the localStorage key prefix, invalidating all active browser sessions. Coordinate with users before changing.
